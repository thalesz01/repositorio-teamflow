using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Executions;
using TeamFlow.Api.Features.Integrations;
using TeamFlow.Api.Features.Tasks;
using Xunit;

namespace TeamFlow.Api.Tests;

public sealed class AgentSelectionTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task SelectionIsValidatedScopedAndPreservedInExecutionAsync()
    {
        using var client = fixture.Factory.CreateClient(); await client.RegisterAsync();
        var project = await client.CreateProjectAsync();
        var cache = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(cache, "{\"models\":[{\"slug\":\"fixture-model\",\"visibility\":\"list\",\"supported_reasoning_levels\":[{\"effort\":\"medium\"}]}]}");
            var configuration = fixture.Factory.Services.GetRequiredService<IConfiguration>();
            configuration["Codex:ModelsCachePath"] = cache; configuration["Codex:Executable"] = LocalIntegrationTests.FixtureExecutable;
            configuration["Codex:EnableRequests"] = "true";
            var path = ApiClient.TasksPath(project) + "/";
            var input = new TaskInput("Selection fixture", "Implement fixture", ["Tests pass"], null, [], Stage: TaskStage.Ready, Ai: new("codex", "fixture-model", "medium"));
            using var invalid = await client.SendAsync(HttpMethod.Post, path, input with { Ai = new("codex", "fixture-model", "max") });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using var created = await client.SendAsync(HttpMethod.Post, path, input);
            var task = await created.ReadAsync<TaskDto>(); Assert.NotNull(task.AgentProfileId);
            using var scope = fixture.Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profile = await db.AgentProfiles.FindAsync(task.AgentProfileId); Assert.NotNull(profile);
            Assert.Equal("fixture-model", profile.Model); Assert.Equal("medium", profile.ReasoningEffort); Assert.True(profile.IsTaskSelection);
            using var scheduled = await client.SendAsync(HttpMethod.Post, path + task.Id + "/executions/", new ExecutionInput(task.Version), Guid.NewGuid().ToString());
            var execution = await scheduled.ReadAsync<ExecutionDto>();
            Assert.Equal(profile.Id, (await db.Executions.FindAsync(execution.Id))?.AgentProfileId);
            configuration["Codex:Model"] = "changed-global-default";
            var reply = await scope.ServiceProvider.GetRequiredService<ProfileAccess>().GenerateAsync(project.OrganizationId, project.Id, profile.Id, "verify-codex-settings", 1, default);
            Assert.Equal("{\"tool\":\"finish\"}", reply.Text);
            // The CLI reports its own consumption; the cost stays unknown.
            Assert.Equal((1000, 800, 20), (reply.InputTokens, reply.CachedInputTokens, reply.OutputTokens)); Assert.Null(reply.EstimatedCostUsd);
        }
        finally { File.Delete(cache); }
    }

    [Fact]
    public async Task TaskCanBeSavedBeforeItsAiIsReadyAndOnlyRunningNeedsTheCliAsync()
    {
        using var client = fixture.Factory.CreateClient(); await client.RegisterAsync();
        var project = await client.CreateProjectAsync();
        var configuration = fixture.Factory.Services.GetRequiredService<IConfiguration>();
        configuration["Codex:Executable"] = ""; configuration["Codex:EnableRequests"] = "true";
        var path = ApiClient.TasksPath(project) + "/";
        using var created = await client.SendAsync(HttpMethod.Post, path,
            new TaskInput("Saved early", "No CLI yet", ["Tests pass"], null, [], Stage: TaskStage.Ready, Ai: new("codex", "default", null)));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var task = await created.ReadAsync<TaskDto>(); Assert.NotNull(task.AgentProfileId);
        using var blocked = await client.SendAsync(HttpMethod.Post, path + task.Id + "/executions/", new ExecutionInput(task.Version), Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, blocked.StatusCode);
        Assert.Equal("cli_not_ready", (await blocked.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("code").GetString());
        using var unchanged = await client.GetAsync(path + task.Id);
        Assert.Equal(TaskStage.Ready, (await unchanged.ReadAsync<TaskDto>()).Stage);
        configuration["Codex:Executable"] = LocalIntegrationTests.FixtureExecutable;
        using var scheduled = await client.SendAsync(HttpMethod.Post, path + task.Id + "/executions/", new ExecutionInput(task.Version), Guid.NewGuid().ToString());
        Assert.True(scheduled.IsSuccessStatusCode);
    }

    [Fact]
    public async Task ClaudeUsesStdinNoToolsAndDoesNotExposeServerSecretsAsync()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Claude:Executable"] = LocalIntegrationTests.FixtureExecutable, ["Claude:EnableRequests"] = "true" }).Build();
        var profile = new AgentProfile { Provider = "claude", Model = "sonnet", ReasoningEffort = "high", TimeoutSeconds = 5, MaxInputBytes = 4096 };
        var model = new ClaudeTextModel(configuration);
        var reply = await model.GenerateAsync(profile, "verify-claude-settings", 1, default);
        Assert.Equal("{\"tool\":\"finish\"}", reply.Text); Assert.Null(reply.EstimatedCostUsd);
        Assert.Null(reply.InputTokens);
        var structured = await model.GenerateAsync(profile, "verify-claude-settings", 1, default, ModelActionSchema.Json);
        Assert.Equal("finish", System.Text.Json.JsonSerializer.Deserialize<ModelAction>(structured.Text, ApiClient.JsonOptions)?.Tool);
        Assert.Equal((602, 500, 4), (structured.InputTokens, structured.CachedInputTokens, structured.OutputTokens));
        var invalid = await Assert.ThrowsAsync<TeamFlow.Api.Http.ApiException>(() => model.GenerateAsync(profile, "invalid-envelope", 1, default, ModelActionSchema.Json));
        Assert.Equal("invalid_model_response", invalid.Code);
        var failure = await Assert.ThrowsAsync<TeamFlow.Api.Http.ApiException>(() => model.GenerateAsync(profile, "fail", 1, default));
        Assert.Equal("claude_failed", failure.Code); Assert.DoesNotContain("sensitive", failure.Message);
    }
}
