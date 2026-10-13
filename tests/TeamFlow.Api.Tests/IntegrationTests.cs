using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Executions;
using TeamFlow.Api.Features.Integrations;
using TeamFlow.Api.Features.Projects;
using TeamFlow.Api.Features.Runners;
using TeamFlow.Api.Features.Tasks;
using Xunit;

namespace TeamFlow.Api.Tests;

public sealed class IntegrationTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    private static string Base(ProjectDto project) => $"/api/v1/organizations/{project.OrganizationId}/projects/{project.Id}";

    [Fact]
    public async Task ProfilesAreScopedToTheirProjectAndRejectForeignOrRevokedSelectionAsync()
    {
        using var client = fixture.Factory.CreateClient();
        using var outsider = fixture.Factory.CreateClient();
        await client.RegisterAsync();
        await outsider.RegisterAsync();
        var project = await client.CreateProjectAsync();
        var profile = await fixture.Factory.Services.SeedProfileAsync(project, "Fixture profile");
        Assert.Equal(new[] { "textGeneration" }, profile.Capabilities);
        Assert.Empty(profile.Tools);
        Assert.Equal("codex", profile.Provider);
        using var list = await client.GetAsync(Base(project) + "/profiles/");
        var json = await list.Content.ReadAsStringAsync();
        Assert.Contains(profile.Id.ToString(), json);
        Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
        using var forbidden = await outsider.GetAsync(Base(project) + "/profiles/");
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
        var task = await ReadyAsync(client, project);
        var other = await client.CreateProjectAsync();
        var foreignTask = await ReadyAsync(client, other);
        using var foreignSelection = await client.SendAsync(HttpMethod.Post, ApiClient.TasksPath(other) + $"/{foreignTask.Id}/executions/",
            new ExecutionInput(foreignTask.Version, AgentProfileId: profile.Id), "foreign-profile");
        Assert.Equal(HttpStatusCode.NotFound, foreignSelection.StatusCode);
        using var scope = fixture.Factory.Services.CreateScope();
        var disabled = await Assert.ThrowsAsync<TeamFlow.Api.Http.ApiException>(() => scope.ServiceProvider.GetRequiredService<ProfileAccess>()
            .GenerateAsync(project.OrganizationId, project.Id, profile.Id, "Fixture prompt", 1m, CancellationToken.None));
        Assert.Equal("integration_not_configured", disabled.Code);
        using var revoke = await client.SendAsync(HttpMethod.Delete, Base(project) + $"/profiles/{profile.Id}", new { });
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        using var revoked = await client.SendAsync(HttpMethod.Post, ApiClient.TasksPath(project) + $"/{task.Id}/executions/",
            new ExecutionInput(task.Version, AgentProfileId: profile.Id), "revoked-profile");
        Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
    }

    [Fact]
    public async Task RepositorySnapshotsAndCheckoutGrantsRequireScopeLeaseAndActiveConnectionAsync()
    {
        using var client = fixture.Factory.CreateClient();
        await client.RegisterAsync();
        var project = await client.CreateProjectAsync();
        var root = Path.Combine(Path.GetTempPath(), "teamflow-grant-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await LocalIntegrationTests.GitAsync(root, ["init", "-b", "main"]);
            await LocalIntegrationTests.GitAsync(root, ["-c", "user.name=Fixture", "-c", "user.email=fixture@example.test", "commit", "--allow-empty", "-m", "Fixture"]);
            fixture.Factory.Services.GetRequiredService<IConfiguration>()[$"Integrations:{project.OrganizationId:D}:{project.Id:D}:Local:RepositoryPath"] = root;
            using var connected = await client.SendAsync(HttpMethod.Post, Base(project) + "/repository/local", new LocalRepositoryInput(root, "main"));
            var repository = await connected.ReadAsync<GitDto>();
            Assert.Equal(40, repository.BaseCommit.Length);
            var task = await ReadyAsync(client, project);
            using var schedule = await client.SendAsync(HttpMethod.Post, ApiClient.TasksPath(project) + $"/{task.Id}/executions/", new ExecutionInput(task.Version), "repository-snapshot");
            var execution = await schedule.ReadAsync<ExecutionDto>();
            using var issue = await client.SendAsync(HttpMethod.Post, Base(project) + "/runners/", new RunnerInput());
            var issued = await issue.ReadAsync<IssuedRunner>();
            using var runner = fixture.Factory.CreateClient();
            runner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", issued.Token);
            using var claim = await runner.PostAsJsonAsync("/internal/v1/executions/claim", new ClaimInput());
            var message = await claim.ReadAsync<JsonElement>();
            var snapshot = message.GetProperty("repository");
            Assert.Equal(repository.BaseCommit, snapshot.GetProperty("baseCommit").GetString());
            Assert.Equal($"teamflow/{execution.Id:N}", snapshot.GetProperty("branch").GetString());
            Assert.Equal(root, snapshot.GetProperty("localPath").GetString());
            var lease = message.GetProperty("leaseToken").GetGuid();
            var grantPath = $"/internal/v1/executions/{execution.Id}/repository-token";
            using var wrongLease = await runner.PostAsJsonAsync(grantPath, new RepositoryTokenInput(Guid.NewGuid()));
            Assert.Equal(HttpStatusCode.Conflict, wrongLease.StatusCode);
            using var grant = await runner.PostAsJsonAsync(grantPath, new RepositoryTokenInput(lease));
            Assert.Equal("local", (await grant.ReadAsync<CheckoutGrant>()).Token);
            // The base branch moving after scheduling must stop checkout instead of silently testing another commit.
            await LocalIntegrationTests.GitAsync(root, ["-c", "user.name=Fixture", "-c", "user.email=fixture@example.test", "commit", "--allow-empty", "-m", "Moved"]);
            using var moved = await runner.PostAsJsonAsync(grantPath, new RepositoryTokenInput(lease));
            Assert.Equal(HttpStatusCode.Conflict, moved.StatusCode);
            Assert.Equal("base_changed", (await moved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
            using var revoke = await client.SendAsync(HttpMethod.Delete, Base(project) + "/repository/", new { });
            Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
            using var noLongerAllowed = await runner.PostAsJsonAsync(grantPath, new RepositoryTokenInput(lease));
            Assert.Equal(HttpStatusCode.Conflict, noLongerAllowed.StatusCode);
            using var scope = fixture.Factory.Services.CreateScope();
            var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Executions.AsNoTracking().SingleAsync(e => e.Id == execution.Id);
            Assert.NotNull(stored.RepositorySnapshot);
            Assert.Equal(repository.BaseCommit, JsonDocument.Parse(stored.RepositorySnapshot).RootElement.GetProperty("baseCommit").GetString());
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<TaskDto> ReadyAsync(HttpClient client, ProjectDto project)
    {
        var task = await client.CreateTaskAsync(project);
        var path = ApiClient.TasksPath(project) + $"/{task.Id}/transition";
        using var analysis = await client.SendAsync(HttpMethod.Post, path, new TransitionInput(TaskStage.Analysis, task.Version));
        task = await analysis.ReadAsync<TaskDto>();
        using var ready = await client.SendAsync(HttpMethod.Post, path, new TransitionInput(TaskStage.Ready, task.Version));
        return await ready.ReadAsync<TaskDto>();
    }
}
