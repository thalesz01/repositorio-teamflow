using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using TeamFlow.Api.Features.Runners;
using TeamFlow.Api.Features.Integrations;
using Xunit;

namespace TeamFlow.Api.Tests;

public sealed class LocalRunnerProvisioningTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task ProvisioningRequiresDevelopmentAndAnAuthorizedProjectAsync()
    {
        using var client = fixture.Factory.CreateClient(); await client.RegisterAsync();
        var project = await client.CreateProjectAsync();
        using var scope = fixture.Factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var environment = new HostEnvironment { EnvironmentName = "Production" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => LocalRunnerProvisioning.IssueAsync(services,
            environment, project.OrganizationId, project.Id, CancellationToken.None));
        environment.EnvironmentName = "Development";
        await Assert.ThrowsAsync<InvalidOperationException>(() => LocalRunnerProvisioning.IssueAsync(services,
            environment, project.OrganizationId, project.Id, CancellationToken.None));
        var root = Path.Combine(Path.GetTempPath(), "teamflow-provision-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var configuration = services.GetRequiredService<IConfiguration>();
            configuration[$"Integrations:{project.OrganizationId}:{project.Id}:Local:RepositoryPath"] = root;
            var path = $"/api/v1/organizations/{project.OrganizationId}/projects/{project.Id}/repository/local/folder";
            using var saved = await client.SendAsync(HttpMethod.Post, path, new LocalFolderInput(root));
            Assert.True(saved.IsSuccessStatusCode);
            configuration[$"Integrations:{project.OrganizationId}:{project.Id}:Local:RepositoryPath"] = root + "-other";
            await Assert.ThrowsAsync<TeamFlow.Api.Http.ApiException>(() => LocalRunnerProvisioning.IssueAsync(services,
                environment, project.OrganizationId, project.Id, CancellationToken.None));
        }
        finally { Directory.Delete(root); }
    }

    private sealed class HostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "";
        public string ApplicationName { get; set; } = "";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
