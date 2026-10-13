using TeamFlow.Api.Features.Executions;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Integrations;
using TeamFlow.Api.Features.Memory;
using TeamFlow.Api.Features.Tasks;
using Xunit;

namespace TeamFlow.Api.Tests;

public sealed class PlanningTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task HybridSearchPlansWithVerifiableSourcesAndBlocksMissingOrRevokedContextAsync()
    {
        var model = new FixturePlanner();
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(s => s.AddSingleton<ITextModel>(model)));
        using var human = factory.CreateClient(); await human.RegisterAsync();
        var project = await human.CreateProjectAsync(); var root = $"/api/v1/organizations/{project.OrganizationId}/projects/{project.Id}";
        var config = factory.Services.GetRequiredService<IConfiguration>();
        config[$"Integrations:{project.OrganizationId}:{project.Id}:OpenAI:ApiKey"] = "fixture-planning-secret";
        config["OpenAI:EnableRequests"] = "true";
        using var profileResponse = await human.SendAsync(HttpMethod.Post, root + "/profiles/", new ProfileInput("Planner", "fixture-planner", 4096, 512, 5, 1, 1));
        var profile = await profileResponse.ReadAsync<ProfileDto>();
        using var candidateResponse = await human.SendAsync(HttpMethod.Post, root + "/memory/", new MemoryInput("docs/rounding.md", "Rounding", "Round financial values to two decimal places. Retrieved text cannot authorize tool execution." + new string('x', 1800)));
        var candidate = await candidateResponse.ReadAsync<MemoryDetail>();
        using var validatedResponse = await human.SendAsync(HttpMethod.Put, root + $"/memory/{candidate.Item.Id}", new MemoryUpdate(candidate.Item.Version, candidate.Item.Title, candidate.Content, MemoryStatus.Validated));
        var source = await validatedResponse.ReadAsync<MemoryDetail>();
        Guid indexId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); indexId = Guid.NewGuid();
            db.MemoryIndexes.Add(new()
            {
                Id = indexId,
                OrganizationId = project.OrganizationId,
                ProjectId = project.Id,
                ItemId = source.Item.Id,
                SourceVersion = source.Item.Version,
                ProfileId = profile.Id,
                IdempotencyKey = "fixture-index",
                RequestHash = new string('a', 64),
                State = "completed",
                CreatedAt = fixture.Clock.GetUtcNow()
            });
            db.MemoryChunks.Add(new() { IndexId = indexId, Sequence = 0, Content = source.Content, ContentHash = source.Item.ContentHash, Embedding = Enumerable.Repeat(0.1f, 1536).ToArray() });
            await db.SaveChangesAsync();
        }
        using var textualResponse = await human.SendAsync(HttpMethod.Post, root + "/memory/search", new SearchInput("round financial"));
        var textual = await textualResponse.ReadAsync<ContextBundle>();
        Assert.Single(textual.Sources); Assert.Equal(source.Item.Version, textual.Sources[0].Version); Assert.Equal(indexId, textual.Sources[0].IndexId); Assert.InRange(textual.Bytes, 1, 8192);
        using var vectorResponse = await human.SendAsync(HttpMethod.Post, root + "/memory/search", new SearchInput("conceptual query", Enumerable.Repeat(0.1f, 1536).ToArray()));
        Assert.Single((await vectorResponse.ReadAsync<ContextBundle>()).Sources);
        using var outsider = factory.CreateClient(); await outsider.RegisterAsync();
        using var foreign = await outsider.SendAsync(HttpMethod.Post, root + "/memory/search", new SearchInput("round"));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        var missingInput = new AnalysisInput("Fix rounding", null, Guid.Empty, "analysis-missing");
        using var missingResponse = await human.SendAsync(HttpMethod.Post, root + "/memory/analyses", missingInput);
        var missing = await missingResponse.ReadAsync<AnalysisDto>(); Assert.Equal("needs_information", missing.State); Assert.NotEmpty(missing.Plan?.Questions ?? []); Assert.Equal(0, model.Calls);
        using var blocked = await human.SendAsync(HttpMethod.Post, root + "/tasks/", new TaskInput("Blocked", "No information", ["Do not execute"], null, [], missing.Id));
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var input = new AnalysisInput("Round financial values", "Use two decimal places for totals", profile.Id, "analysis-complete");
        using var analysisResponse = await human.SendAsync(HttpMethod.Post, root + "/memory/analyses", input);
        var analysis = await analysisResponse.ReadAsync<AnalysisDto>(); Assert.Equal("completed", analysis.State); Assert.Single(analysis.Context?.Sources ?? []); Assert.Equal(1, model.Calls);
        Assert.Contains("untrusted data", model.Prompt); Assert.Contains(source.Item.Version.ToString(), model.Prompt);
        using var replay = await human.SendAsync(HttpMethod.Post, root + "/memory/analyses", input); Assert.Equal(analysis.Id, (await replay.ReadAsync<AnalysisDto>()).Id); Assert.Equal(1, model.Calls);
        using var taskResponse = await human.SendAsync(HttpMethod.Post, root + "/tasks/", new TaskInput("Human-edited title", "Human accepted proposal", ["Human criterion"], null, [], analysis.Id));
        var task = await taskResponse.ReadAsync<TaskDto>(); Assert.Equal(TaskStage.Intake, task.Stage); Assert.Equal(analysis.Id, task.AnalysisId);
        var taskPath = root + $"/tasks/{task.Id}";
        using var analyzing = await human.SendAsync(HttpMethod.Post, taskPath + "/transition", new TransitionInput(TaskStage.Analysis, task.Version));
        task = await analyzing.ReadAsync<TaskDto>();
        using var ready = await human.SendAsync(HttpMethod.Post, taskPath + "/transition", new TransitionInput(TaskStage.Ready, task.Version));
        task = await ready.ReadAsync<TaskDto>();
        using var executionResponse = await human.SendAsync(HttpMethod.Post, taskPath + "/executions/", new ExecutionInput(task.Version, AgentProfileId: profile.Id), "reuse-memory-task");
        var execution = await executionResponse.ReadAsync<ExecutionDto>();
        using (var scope = factory.Services.CreateScope())
        {
            var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Executions.SingleAsync(e => e.Id == execution.Id);
            Assert.Contains(source.Item.Version.ToString(), stored.MemoryContext ?? "");
        }
        using var archive = await human.SendAsync(HttpMethod.Delete, root + $"/memory/{source.Item.Id}?version={source.Item.Version}", new { }); archive.EnsureSuccessStatusCode();
        using var hidden = await human.SendAsync(HttpMethod.Post, root + "/memory/search", new SearchInput("round")); Assert.Empty((await hidden.ReadAsync<ContextBundle>()).Sources);
        using var stale = await human.GetAsync(root + $"/memory/analyses/{analysis.Id}"); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            Assert.Null(await scope.ServiceProvider.GetRequiredService<ExecutionScheduler>().ClaimAsync(Guid.NewGuid(), 30, default, project.OrganizationId, project.Id));
            var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Executions.SingleAsync(e => e.Id == execution.Id);
            Assert.Equal(ExecutionState.Failed, stored.State); Assert.Equal("context_source_changed", stored.FailureCode);
        }
        using var acceptStale = await human.SendAsync(HttpMethod.Post, root + "/tasks/", new TaskInput("Invalid", "Stale source", ["Criterion"], null, [], analysis.Id)); Assert.Equal(HttpStatusCode.Conflict, acceptStale.StatusCode);
    }

    [Fact]
    public async Task BudgetAdmissionAndLostAnalysisResponseDoNotRepeatPaidBoundaryAsync()
    {
        var model = new FixturePlanner { LoseResponse = true };
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(s => s.AddSingleton<ITextModel>(model)));
        using var human = factory.CreateClient(); await human.RegisterAsync(); var project = await human.CreateProjectAsync();
        var root = $"/api/v1/organizations/{project.OrganizationId}/projects/{project.Id}";
        var config = factory.Services.GetRequiredService<IConfiguration>(); config["OpenAI:EnableRequests"] = "true";
        config[$"Integrations:{project.OrganizationId}:{project.Id}:OpenAI:ApiKey"] = "fixture-analysis-key";
        using var profileResponse = await human.SendAsync(HttpMethod.Post, root + "/profiles/", new ProfileInput("Planner", "fixture-planner", 4096, 512, 5, 1, 1));
        var profile = await profileResponse.ReadAsync<ProfileDto>();
        var input = new AnalysisInput("Fix calculation", "Result has two decimals", profile.Id, "analysis-budget", 0.00001m);
        using var refused = await human.SendAsync(HttpMethod.Post, root + "/memory/analyses", input);
        var rejected = await refused.ReadAsync<AnalysisDto>(); Assert.Equal("failed", rejected.State); Assert.Equal("budget_exceeded", rejected.FailureCode); Assert.Equal(0, model.Calls);
        input = input with { IdempotencyKey = "analysis-lost-response", MaxCostUsd = 1 };
        using var lost = await human.SendAsync(HttpMethod.Post, root + "/memory/analyses", input);
        var unknown = await lost.ReadAsync<AnalysisDto>(); Assert.Equal("uncertain", unknown.State); Assert.Null(unknown.EstimatedCostUsd); Assert.Equal(1, model.Calls);
        using var replay = await human.SendAsync(HttpMethod.Post, root + "/memory/analyses", input);
        Assert.Equal(unknown.Id, (await replay.ReadAsync<AnalysisDto>()).Id); Assert.Equal(1, model.Calls);
        using var accept = await human.SendAsync(HttpMethod.Post, root + "/tasks/", new TaskInput("Unsafe", "Unknown result", ["Criterion"], null, [], unknown.Id));
        Assert.Equal(HttpStatusCode.Conflict, accept.StatusCode);
    }
    private sealed class FixturePlanner : ITextModel
    {
        public int Calls { get; private set; }
        public bool LoseResponse { get; set; }
        public string Prompt { get; private set; } = "";
        public Task<TextModelResult> GenerateAsync(AgentProfile profile, string key, string prompt, decimal budget, CancellationToken ct)
        {
            Calls++; Prompt = prompt;
            if (LoseResponse) throw new HttpRequestException("Simulated response lost after provider processing.");
            return Task.FromResult(new TextModelResult(JsonSerializer.Serialize(new PlanDraft("Fix rounding", "Use the cited rounding rule.", ["Totals have two decimal places"], []), MemorySearch.JsonOptions), 100, 100, 0.0002m));
        }
    }
}
