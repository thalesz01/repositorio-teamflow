using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Executions;
using TeamFlow.Api.Features.Integrations;
using TeamFlow.Api.Features.Projects;
using TeamFlow.Api.Features.Runners;
using TeamFlow.Api.Features.Tasks;
using TeamFlow.Api.Http;
using Xunit;

namespace TeamFlow.Api.Tests;

public sealed class IntegrationTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    private static void Configure(IConfiguration configuration, ProjectDto project)
    {
        var scope = $"Integrations:{project.OrganizationId:D}:{project.Id:D}:";
        configuration[scope + "OpenAI:ApiKey"] = "fixture-openai-secret";
        configuration[scope + "GitHub:AppId"] = "123";
        configuration[scope + "GitHub:InstallationId"] = "456";
        configuration[scope + "GitHub:RepositoryId"] = "789";
        configuration[scope + "GitHub:PrivateKeyPem"] = "fixture-private-key";
    }
    private static string Base(ProjectDto project) => $"/api/v1/organizations/{project.OrganizationId}/projects/{project.Id}";
    private static ProfileInput Profile => new("Fixture profile", "fixture-model", 4096, 128, 5, 1, 2);

    [Fact]
    public async Task ProfilesProtectSecretsAndRejectForeignOrRevokedSelectionAsync()
    {
        using var client = fixture.Factory.CreateClient();
        using var outsider = fixture.Factory.CreateClient();
        await client.RegisterAsync();
        await outsider.RegisterAsync();
        var project = await client.CreateProjectAsync();
        Configure(fixture.Factory.Services.GetRequiredService<IConfiguration>(), project);
        using var create = await client.SendAsync(HttpMethod.Post, Base(project) + "/profiles/", Profile);
        var profile = await create.ReadAsync<ProfileDto>();
        Assert.Equal(new[] { "textGeneration" }, profile.Capabilities);
        Assert.Empty(profile.Tools);
        using var list = await client.GetAsync(Base(project) + "/profiles/");
        var json = await list.Content.ReadAsStringAsync();
        Assert.DoesNotContain("fixture-openai-secret", json);
        Assert.DoesNotContain("protectedApiKey", json);
        using var forbidden = await outsider.GetAsync(Base(project) + "/profiles/");
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.AgentProfiles.AsNoTracking().SingleAsync(p => p.Id == profile.Id);
        Assert.NotEqual("fixture-openai-secret", stored.ProtectedApiKey);
        var secrets = scope.ServiceProvider.GetRequiredService<IntegrationSecrets>();
        Assert.Equal("fixture-openai-secret", secrets.Unprotect(project.OrganizationId, project.Id, profile.Id, "openai", stored.ProtectedApiKey));
        Assert.Equal("secret_unavailable", Assert.Throws<ApiException>(() => secrets.Unprotect(project.OrganizationId, Guid.NewGuid(), profile.Id, "openai", stored.ProtectedApiKey)).Code);
        var task = await ReadyAsync(client, project);
        var other = await client.CreateProjectAsync();
        var foreignTask = await ReadyAsync(client, other);
        using var foreignSelection = await client.SendAsync(HttpMethod.Post, ApiClient.TasksPath(other) + $"/{foreignTask.Id}/executions/",
            new ExecutionInput(foreignTask.Version, AgentProfileId: profile.Id), "foreign-profile");
        Assert.Equal(HttpStatusCode.NotFound, foreignSelection.StatusCode);
        var disabled = await Assert.ThrowsAsync<ApiException>(() => scope.ServiceProvider.GetRequiredService<ProfileAccess>()
            .GenerateAsync(project.OrganizationId, project.Id, profile.Id, "Fixture prompt", 1m, CancellationToken.None));
        Assert.Equal("provider_disabled", disabled.Code);
        using var revoke = await client.SendAsync(HttpMethod.Delete, Base(project) + $"/profiles/{profile.Id}", new { });
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        using var revoked = await client.SendAsync(HttpMethod.Post, ApiClient.TasksPath(project) + $"/{task.Id}/executions/",
            new ExecutionInput(task.Version, AgentProfileId: profile.Id), "revoked-profile");
        Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
        Assert.Equal("", (await db.AgentProfiles.AsNoTracking().SingleAsync(p => p.Id == profile.Id)).ProtectedApiKey);
    }

    [Fact]
    public async Task RepositorySnapshotsAndTokensRequireScopeLeaseAndActiveConnectionAsync()
    {
        var git = new FixtureGitProvider();
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<IGitProvider>(git)));
        using var client = factory.CreateClient();
        await client.RegisterAsync();
        var project = await client.CreateProjectAsync();
        Configure(factory.Services.GetRequiredService<IConfiguration>(), project);
        var input = new GitInput(456, 789, "fixture", "repository", "main");
        using var unauthorized = await client.SendAsync(HttpMethod.Post, Base(project) + "/repository/", input with { RepositoryId = 999 });
        Assert.Equal(HttpStatusCode.Forbidden, unauthorized.StatusCode);
        Assert.Equal(0, git.Verifications);
        using var invalid = await client.SendAsync(HttpMethod.Post, Base(project) + "/repository/", input with { Owner = "../other" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var connected = await client.SendAsync(HttpMethod.Post, Base(project) + "/repository/", input);
        var repository = await connected.ReadAsync<GitDto>();
        Assert.Equal(new string('a', 40), repository.BaseCommit);
        using var detail = await client.GetAsync(Base(project) + "/repository/");
        Assert.DoesNotContain("fixture-private-key", await detail.Content.ReadAsStringAsync());
        var task = await ReadyAsync(client, project);
        using var schedule = await client.SendAsync(HttpMethod.Post, ApiClient.TasksPath(project) + $"/{task.Id}/executions/", new ExecutionInput(task.Version), "repository-snapshot");
        var execution = await schedule.ReadAsync<ExecutionDto>();
        using var issue = await client.SendAsync(HttpMethod.Post, Base(project) + "/runners/", new RunnerInput());
        var issued = await issue.ReadAsync<IssuedRunner>();
        using var runner = factory.CreateClient();
        runner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", issued.Token);
        using var claim = await runner.PostAsJsonAsync("/internal/v1/executions/claim", new ClaimInput());
        var message = await claim.ReadAsync<JsonElement>();
        var snapshot = message.GetProperty("repository");
        Assert.Equal(repository.BaseCommit, snapshot.GetProperty("baseCommit").GetString());
        Assert.Equal($"teamflow/{execution.Id:N}", snapshot.GetProperty("branch").GetString());
        var lease = message.GetProperty("leaseToken").GetGuid();
        var tokenPath = $"/internal/v1/executions/{execution.Id}/repository-token";
        using var wrongLease = await runner.PostAsJsonAsync(tokenPath, new RepositoryTokenInput(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Conflict, wrongLease.StatusCode);
        using var token = await runner.PostAsJsonAsync(tokenPath, new RepositoryTokenInput(lease));
        Assert.Equal("fixture-checkout-token", (await token.ReadAsync<InstallationToken>()).Token);
        git.BeforeToken = async () =>
        {
            using var revocationScope = factory.Services.CreateScope();
            await revocationScope.ServiceProvider.GetRequiredService<AppDbContext>().RunnerCredentials
                .Where(r => r.Id == issued.Credential.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(r => r.RevokedAt, fixture.Clock.GetUtcNow()));
        };
        using var revokedDuringMint = await runner.PostAsJsonAsync(tokenPath, new RepositoryTokenInput(lease));
        Assert.Equal(HttpStatusCode.Unauthorized, revokedDuringMint.StatusCode);
        git.BeforeToken = null;
        using var secondIssue = await client.SendAsync(HttpMethod.Post, Base(project) + "/runners/", new RunnerInput());
        var replacement = await secondIssue.ReadAsync<IssuedRunner>();
        runner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", replacement.Token);
        fixture.Clock.Advance(TimeSpan.FromSeconds(31));
        using var reclaimed = await runner.PostAsJsonAsync("/internal/v1/executions/claim", new ClaimInput());
        var recovered = await reclaimed.ReadAsync<JsonElement>();
        lease = recovered.GetProperty("leaseToken").GetGuid();
        using var revoke = await client.SendAsync(HttpMethod.Delete, Base(project) + "/repository/", new { });
        using var noLongerAllowed = await runner.PostAsJsonAsync(tokenPath, new RepositoryTokenInput(lease));
        Assert.Equal(HttpStatusCode.Conflict, noLongerAllowed.StatusCode);
        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Executions.AsNoTracking().SingleAsync(e => e.Id == execution.Id);
        Assert.NotNull(stored.RepositorySnapshot);
        Assert.DoesNotContain("fixture-checkout-token", stored.RepositorySnapshot);
        Assert.Equal(snapshot.GetProperty("baseCommit").GetString(), JsonDocument.Parse(stored.RepositorySnapshot).RootElement.GetProperty("baseCommit").GetString());
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

public sealed class FixtureGitProvider : IGitProvider
{
    public int Verifications { get; private set; }
    public Func<Task>? BeforeToken { get; set; }
    public Task<string> VerifyAsync(GitConnection connection, string privateKey, CancellationToken ct)
    { Verifications++; return Task.FromResult(new string('a', 40)); }
    public async Task<InstallationToken> TokenAsync(GitConnection connection, string privateKey, CancellationToken ct)
    {
        if (BeforeToken is { } callback) await callback();
        return new("fixture-checkout-token", DateTimeOffset.UtcNow.AddMinutes(10));
    }
}
