using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Executions;
using TeamFlow.Api.Features.Integrations;
using TeamFlow.Api.Features.Projects;
using TeamFlow.Api.Features.Runners;
using TeamFlow.Api.Features.Tasks;
using Xunit;

namespace TeamFlow.Api.Tests;

public sealed class AgentRuntimeTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task PaidOperationIsIdempotentAndCumulativeBudgetStopsNextCallAsync()
    {
        var model = new FixtureModel { Cost = 0.009m };
        using var setup = await SetupAsync(model, 0.01m);
        var input = new ModelTurnInput(setup.Lease, Guid.NewGuid(), 0, new(["answer.txt"], ["answer.txt"]));
        using var first = await setup.Runner.PostAsJsonAsync(setup.Path + "/model-turn", input);
        Assert.Equal("read", (await first.ReadAsync<ModelTurnReply>()).Action.Tool);
        using var replay = await setup.Runner.PostAsJsonAsync(setup.Path + "/model-turn", input);
        Assert.Equal(input.OperationId, (await replay.ReadAsync<ModelTurnReply>()).OperationId);
        Assert.Equal(1, model.Calls);
        var next = setup.Context with { Evidence = [new(1, "read", "answer.txt", true, "41", new string('a', 64))] };
        using var checkpoint = await setup.Runner.PutAsJsonAsync(setup.Path + "/checkpoint/", new CheckpointInput(setup.Lease, next));
        checkpoint.EnsureSuccessStatusCode();
        using var budget = await setup.Runner.PostAsJsonAsync(setup.Path + "/model-turn", input with { OperationId = Guid.NewGuid(), Sequence = 1 });
        Assert.Equal(HttpStatusCode.Conflict, budget.StatusCode);
        Assert.Equal("budget_exceeded", (await budget.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(1, model.Calls);
    }

    [Fact]
    public async Task ConfirmedCliAnswerCanReplayAndRecoverWithUnknownBillingAsync()
    {
        using var setup = await SetupAsync(null, inputUsd: 0, outputUsd: 0);
        var configuration = setup.Factory.Services.GetRequiredService<IConfiguration>();
        configuration["Codex:Executable"] = LocalIntegrationTests.FixtureExecutable;
        var input = new ModelTurnInput(setup.Lease, Guid.NewGuid(), 0, new(["answer.txt"], ["answer.txt"]));
        using var first = await setup.Runner.PostAsJsonAsync(setup.Path + "/model-turn", input);
        var reply = await first.ReadAsync<ModelTurnReply>();
        Assert.Equal("finish", reply.Action.Tool); Assert.Null(reply.EstimatedCostUsd);
        using var replay = await setup.Runner.PostAsJsonAsync(setup.Path + "/model-turn", input);
        Assert.Equal(input.OperationId, (await replay.ReadAsync<ModelTurnReply>()).OperationId);
        using var recovery = await setup.Runner.PostAsJsonAsync(setup.Path + "/recovery", new RecoveryInput(setup.Lease));
        recovery.EnsureSuccessStatusCode();
        using var checkScope = setup.Factory.Services.CreateScope();
        var turn = await checkScope.ServiceProvider.GetRequiredService<AppDbContext>().ModelTurns.SingleAsync(t => t.ExecutionId == setup.Execution.Id);
        Assert.Equal("cli", turn.BillingMode); Assert.Null(turn.EstimatedCostUsd); Assert.Equal(0, turn.ReservedUsd);
    }

    [Fact]
    public async Task UnknownCliConsumptionStaysUnknownAndDoesNotBlockLaterTurnsAsync()
    {
        var model = new FixtureModel { Cost = null };
        using var setup = await SetupAsync(model);
        var input = new ModelTurnInput(setup.Lease, Guid.NewGuid(), 0, new(["answer.txt"], ["answer.txt"]));
        using var first = await setup.Runner.PostAsJsonAsync(setup.Path + "/model-turn", input);
        Assert.Null((await first.ReadAsync<ModelTurnReply>()).EstimatedCostUsd);
        Assert.Equal(1, model.Calls);
        using var scope = setup.Factory.Services.CreateScope();
        var turn = await scope.ServiceProvider.GetRequiredService<AppDbContext>().ModelTurns.SingleAsync(t => t.ExecutionId == setup.Execution.Id);
        Assert.Equal("cli", turn.BillingMode);
        Assert.Null(turn.EstimatedCostUsd);
        Assert.True(turn.ReservedUsd > 0);
    }

    [Fact]
    public async Task HistoricalMeteredTurnWithoutKnownCostStillBlocksRetryAsync()
    {
        var model = new FixtureModel();
        using var setup = await SetupAsync(model);
        using (var scope = setup.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ModelTurns.Add(new ModelTurn
            {
                ExecutionId = setup.Execution.Id,
                OperationId = Guid.NewGuid(),
                OrganizationId = setup.Project.OrganizationId,
                ProjectId = setup.Project.Id,
                Attempt = 1,
                Sequence = 5,
                RequestHash = new string('c', 64),
                BillingMode = "api",
                State = "completed"
            });
            await db.SaveChangesAsync();
        }
        using var blocked = await setup.Runner.PostAsJsonAsync(setup.Path + "/model-turn",
            new ModelTurnInput(setup.Lease, Guid.NewGuid(), 0, new(["answer.txt"], ["answer.txt"])));
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("model_outcome_uncertain", (await blocked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public async Task PendingCallCannotBeDuplicatedAndExpiredLeaseCannotReceiveActionAsync()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new FixtureModel { BeforeReply = async ct => { started.SetResult(); await release.Task.WaitAsync(ct); } };
        using var setup = await SetupAsync(model);
        var input = new ModelTurnInput(setup.Lease, Guid.NewGuid(), 0, new(["answer.txt"], ["answer.txt"]));
        var pending = setup.Runner.PostAsJsonAsync(setup.Path + "/model-turn", input);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var duplicate = await setup.Runner.PostAsJsonAsync(setup.Path + "/model-turn", input with { OperationId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        fixture.Clock.Advance(TimeSpan.FromSeconds(121));
        release.SetResult();
        using var lost = await pending;
        Assert.Equal(HttpStatusCode.Conflict, lost.StatusCode);
        Assert.Equal(1, model.Calls);
        using var scope = setup.Factory.Services.CreateScope();
        Assert.Equal("uncertain", (await scope.ServiceProvider.GetRequiredService<AppDbContext>().ModelTurns.SingleAsync(t => t.ExecutionId == setup.Execution.Id)).State);
    }

    [Fact]
    public async Task ResultRequiresEvidenceAndReplayDoesNotCompleteTaskOrDuplicateStateAsync()
    {
        using var setup = await SetupAsync(new FixtureModel());
        var result = new ExecutionResultInput(setup.Lease, Guid.NewGuid(), 0, true, "tests_passed");
        using var premature = await setup.Runner.PostAsJsonAsync(setup.Path + "/result", result);
        Assert.Equal(HttpStatusCode.Conflict, premature.StatusCode);
        var context = setup.Context;
        foreach (var evidence in new CheckpointEvidence[]
        {
            new(1, "read", "answer.txt", true, "41", new string('a', 64)),
            new(2, "write", "answer.txt", true, "file_updated", new string('b', 64)),
            new(3, "test", null, true, "exit=0; timeout=False; truncated=False")
        })
        {
            context = context with { Evidence = [.. context.Evidence, evidence] };
            using var checkpoint = await setup.Runner.PutAsJsonAsync(setup.Path + "/checkpoint/", new CheckpointInput(setup.Lease, context));
            checkpoint.EnsureSuccessStatusCode();
        }
        result = result with { CheckpointSequence = 3 };
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => setup.Runner.PostAsJsonAsync(setup.Path + "/result", result)));
        foreach (var response in responses) { using (response) Assert.True((await response.ReadAsync<ExecutionResultReply>()).Validated); }
        fixture.Clock.Advance(TimeSpan.FromMinutes(20));
        using var replay = await setup.Runner.PostAsJsonAsync(setup.Path + "/result", result);
        Assert.Equal(result.OperationId, (await replay.ReadAsync<ExecutionResultReply>()).OperationId);
        using var different = await setup.Runner.PostAsJsonAsync(setup.Path + "/result", result with { OperationId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
        using var scope = setup.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(ExecutionState.Succeeded, (await db.Executions.SingleAsync(e => e.Id == setup.Execution.Id)).State);
        Assert.Equal(TaskStage.Execution, (await db.WorkItems.SingleAsync(t => t.Id == setup.Execution.TaskId)).Stage);
    }

    [Fact]
    public async Task FailedResultAllowsNewExecutionWhilePreservingHistoryAsync()
    {
        using var setup = await SetupAsync(new FixtureModel());
        using var failed = await setup.Runner.PostAsJsonAsync(setup.Path + "/result",
            new ExecutionResultInput(setup.Lease, Guid.NewGuid(), 0, false, "runner_failed"));
        Assert.False((await failed.ReadAsync<ExecutionResultReply>()).Validated);
        var taskPath = ApiClient.TasksPath(setup.Project) + $"/{setup.Execution.TaskId}";
        using var taskResponse = await setup.Human.GetAsync(taskPath);
        var task = await taskResponse.ReadAsync<TaskDto>();
        Assert.Equal(TaskStage.Failed, task.Stage);
        using var ready = await setup.Human.SendAsync(HttpMethod.Post, taskPath + "/transition", new TransitionInput(TaskStage.Ready, task.Version));
        task = await ready.ReadAsync<TaskDto>();
        using var next = await setup.Human.SendAsync(HttpMethod.Post, taskPath + "/executions/", new ExecutionInput(task.Version), Guid.NewGuid().ToString());
        Assert.NotEqual(setup.Execution.Id, (await next.ReadAsync<ExecutionDto>()).Id);
        using var scope = setup.Factory.Services.CreateScope();
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<AppDbContext>().Executions.CountAsync(e => e.TaskId == task.Id));
    }

    [Fact]
    public async Task RecoveryReusesKnownActionAcrossAttemptsWithoutCallingProviderAgainAsync()
    {
        var model = new FixtureModel();
        using var setup = await SetupAsync(model);
        var policy = new AgentToolPolicy(["answer.txt"], ["answer.txt"]);
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("41")));
        var context = setup.Context with { Evidence = [new(1, "read", "answer.txt", true, "41", hash)] };
        using var checkpoint = await setup.Runner.PutAsJsonAsync(setup.Path + "/checkpoint/", new CheckpointInput(setup.Lease, context));
        checkpoint.EnsureSuccessStatusCode();
        model.Text = JsonSerializer.Serialize(new ModelAction("write", "answer.txt", "42", hash), ApiClient.JsonOptions);
        using var generated = await setup.Runner.PostAsJsonAsync(setup.Path + "/model-turn",
            new ModelTurnInput(setup.Lease, Guid.NewGuid(), 1, policy));
        Assert.Equal("write", (await generated.ReadAsync<ModelTurnReply>()).Action.Tool);
        fixture.Clock.Advance(TimeSpan.FromSeconds(121));
        using var claim = await setup.Runner.PostAsJsonAsync("/internal/v1/executions/claim", new ClaimInput(120));
        var token = (await claim.ReadAsync<JsonElement>()).GetProperty("leaseToken").GetGuid();
        using var plan = await setup.Runner.PostAsJsonAsync(setup.Path + "/recovery", new RecoveryInput(token));
        Assert.Equal(1, (await plan.ReadAsync<RecoveryReply>()).Checkpoint?.Sequence);
        using var inherited = await setup.Runner.PutAsJsonAsync(setup.Path + "/checkpoint/", new CheckpointInput(token, context));
        Assert.Equal(2, (await inherited.ReadAsync<CheckpointReply>()).Attempt);
        var operation = Guid.NewGuid();
        using var reused = await setup.Runner.PostAsJsonAsync(setup.Path + "/model-turn", new ModelTurnInput(token, operation, 1, policy));
        var reply = await reused.ReadAsync<ModelTurnReply>();
        Assert.Equal(operation, reply.OperationId);
        Assert.Equal("42", reply.Action.Content);
        Assert.Equal(1, model.Calls);
    }

    [Fact]
    public async Task CancelAndDeadlineStopTheCurrentAttemptAndPersistTheirReasonAsync()
    {
        using var setup = await SetupAsync(new FixtureModel());
        var taskPath = ApiClient.TasksPath(setup.Project) + $"/{setup.Execution.TaskId}";
        using var cancel = await setup.Human.SendAsync(HttpMethod.Post, taskPath + $"/executions/{setup.Execution.Id}/cancel", new { });
        cancel.EnsureSuccessStatusCode();
        using var stopped = await setup.Runner.PostAsJsonAsync(setup.Path + "/result", new ExecutionResultInput(setup.Lease, Guid.NewGuid(), 0, false, "cancelled"));
        Assert.Equal("cancelled", (await stopped.ReadAsync<ExecutionResultReply>()).Reason);
        using var scope = setup.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(ExecutionState.Cancelled, (await db.Executions.SingleAsync(e => e.Id == setup.Execution.Id)).State);
        using var timed = await SetupAsync(new FixtureModel());
        fixture.Clock.Advance(TimeSpan.FromSeconds(901));
        using var exhausted = await timed.Runner.PostAsJsonAsync(timed.Path + "/result", new ExecutionResultInput(timed.Lease, Guid.NewGuid(), 0, false, "deadline_exceeded"));
        Assert.Equal("deadline_exceeded", (await exhausted.ReadAsync<ExecutionResultReply>()).Reason);
        Assert.Equal("deadline_exceeded", (await db.Executions.SingleAsync(e => e.Id == timed.Execution.Id)).FailureCode);
    }

    [Fact]
    public async Task ProgressIsScopedResumableAndNeverReturnsRawEvidenceAsync()
    {
        using var setup = await SetupAsync(new FixtureModel());
        var context = setup.Context with { Evidence = [new(1, "read", "answer.txt", true, "fixture-secret-do-not-display", new string('b', 64))] };
        using var saved = await setup.Runner.PutAsJsonAsync(setup.Path + "/checkpoint/", new CheckpointInput(setup.Lease, context));
        saved.EnsureSuccessStatusCode();
        var path = ApiClient.TasksPath(setup.Project) + $"/{setup.Execution.TaskId}/executions/{setup.Execution.Id}";
        using var response = await setup.Human.GetAsync(path + "/progress");
        var progress = await response.ReadAsync<ExecutionProgress>();
        Assert.Equal(2, progress.Events.Length);
        Assert.Equal(2, progress.Events.Select(e => e.EventId).Distinct().Count());
        Assert.DoesNotContain("fixture-secret", await response.Content.ReadAsStringAsync());
        using var report = await setup.Human.GetAsync(path + "/reports/1001");
        report.EnsureSuccessStatusCode();
        Assert.DoesNotContain("fixture-secret", await report.Content.ReadAsStringAsync());
        Assert.Equal("application/json", report.Content.Headers.ContentType?.MediaType);
        Assert.True((await report.Content.ReadAsByteArrayAsync()).Length < 4096);
        using var resumed = await setup.Human.GetAsync(path + "/progress?after=" + progress.NextCursor);
        Assert.Empty((await resumed.ReadAsync<ExecutionProgress>()).Events);
        using var invalid = await setup.Human.GetAsync(path + "/progress?after=-1");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var foreign = setup.Factory.CreateClient();
        await foreign.RegisterAsync();
        using var denied = await foreign.GetAsync(path + "/progress");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var deniedReport = await foreign.GetAsync(path + "/reports/1001");
        Assert.Equal(HttpStatusCode.NotFound, deniedReport.StatusCode);
        using var stream = await setup.Human.GetAsync(path + "/events", HttpCompletionOption.ResponseHeadersRead);
        stream.EnsureSuccessStatusCode();
        Assert.Equal("text/event-stream", stream.Content.Headers.ContentType?.MediaType);
        using var reader = new StreamReader(await stream.Content.ReadAsStreamAsync());
        Assert.Equal("id: 1000", await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        stream.Dispose();
        using var scope = setup.Factory.Services.CreateScope();
        var execution = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Executions.SingleAsync(e => e.Id == setup.Execution.Id);
        var user = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new(System.Security.Claims.ClaimTypes.NameIdentifier, execution.RequestedBy.ToString())], "fixture"));
        fixture.Clock.Advance(TimeSpan.FromDays(31));
        try
        {
            var expired = await Assert.ThrowsAsync<TeamFlow.Api.Http.ApiException>(() => scope.ServiceProvider.GetRequiredService<ProgressAccess>()
                .ReportAsync(setup.Project.OrganizationId, setup.Project.Id, setup.Execution.TaskId, setup.Execution.Id, "1001", user, default));
            Assert.Equal("report_not_found", expired.Code);
        }
        finally { fixture.Clock.Advance(TimeSpan.FromDays(-31)); }
    }

    [Fact]
    public async Task ProgressKeepsUnknownConsumptionUnknownAsync()
    {
        using var setup = await SetupAsync(new FixtureModel { Cost = null });
        using var generated = await setup.Runner.PostAsJsonAsync(setup.Path + "/model-turn",
            new ModelTurnInput(setup.Lease, Guid.NewGuid(), 0, new(["answer.txt"], ["answer.txt"])));
        Assert.Null((await generated.ReadAsync<ModelTurnReply>()).EstimatedCostUsd);
        var path = ApiClient.TasksPath(setup.Project) + $"/{setup.Execution.TaskId}/executions/{setup.Execution.Id}/progress";
        using var response = await setup.Human.GetAsync(path);
        var progress = await response.ReadAsync<ExecutionProgress>();
        Assert.Null(progress.EstimatedCostUsd);
        Assert.Null(progress.InputTokens);
        Assert.Null(progress.OutputTokens);
    }
    [Fact]
    public async Task PublicationRequiresValidationAndReconcilesBeforeMovingTaskToReviewAsync()
    {
        var publisher = new FixturePublisher();
        var model = new FixtureModel();
        using var setup = await SetupAsync(model, publisher: publisher);
        var path = ApiClient.TasksPath(setup.Project) + $"/{setup.Execution.TaskId}/executions/{setup.Execution.Id}/publication";
        var config = setup.Factory.Services.GetRequiredService<IConfiguration>();
        config["Local:EnablePublication"] = "true"; // The publisher is the explicit in-memory double; no Git repository is touched.
        using var premature = await setup.Human.SendAsync(HttpMethod.Post, path, new { });
        Assert.Equal(HttpStatusCode.Conflict, premature.StatusCode);
        Assert.Equal(0, publisher.Calls);
        using (var scope = setup.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var execution = await db.Executions.SingleAsync(e => e.Id == setup.Execution.Id);
            var repository = JsonSerializer.Deserialize<RepositorySnapshot>(execution.RepositorySnapshot ?? "", ApiClient.JsonOptions)
                ?? throw new InvalidDataException("Missing fixture repository.");
            db.GitConnections.Add(new GitConnection
            {
                Id = repository.ConnectionId,
                Version = repository.ConnectionVersion,
                OrganizationId = setup.Project.OrganizationId,
                ProjectId = setup.Project.Id,
                RepositoryId = 1,
                Provider = "local",
                LocalPath = repository.RemoteUrl,
                BaseBranch = "main",
                BaseCommit = repository.BaseCommit
            });
            await db.SaveChangesAsync();
        }
        var beforeHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("41")));
        var afterHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("42")));
        var context = setup.Context with { Evidence = [new(1, "read", "answer.txt", true, "41", beforeHash)] };
        using var read = await setup.Runner.PutAsJsonAsync(setup.Path + "/checkpoint/", new CheckpointInput(setup.Lease, context));
        read.EnsureSuccessStatusCode();
        model.Text = JsonSerializer.Serialize(new ModelAction("write", "answer.txt", "42", beforeHash), ApiClient.JsonOptions);
        using var action = await setup.Runner.PostAsJsonAsync(setup.Path + "/model-turn", new ModelTurnInput(setup.Lease, Guid.NewGuid(), 1, new(["answer.txt"], ["answer.txt"])));
        action.EnsureSuccessStatusCode();
        context = context with { Evidence = [.. context.Evidence, new(2, "write", "answer.txt", true, "file_written", afterHash)] };
        using var write = await setup.Runner.PutAsJsonAsync(setup.Path + "/checkpoint/", new CheckpointInput(setup.Lease, context));
        write.EnsureSuccessStatusCode();
        context = context with { Evidence = [.. context.Evidence, new(3, "test", null, true, "fixture_test_passed")] };
        using var tested = await setup.Runner.PutAsJsonAsync(setup.Path + "/checkpoint/", new CheckpointInput(setup.Lease, context));
        tested.EnsureSuccessStatusCode();
        using var completed = await setup.Runner.PostAsJsonAsync(setup.Path + "/result", new ExecutionResultInput(setup.Lease, Guid.NewGuid(), 3, true, "tests_passed"));
        completed.EnsureSuccessStatusCode();
        using var lost = await setup.Human.SendAsync(HttpMethod.Post, path, new { });
        Assert.Equal(HttpStatusCode.BadGateway, lost.StatusCode);
        using var retried = await setup.Human.SendAsync(HttpMethod.Post, path, new { });
        var published = await retried.ReadAsync<PublicationStatus>();
        Assert.Equal("published", published.State);
        Assert.Equal(2, published.Attempts);
        Assert.NotNull(published.PullRequest);
        using var replay = await setup.Human.SendAsync(HttpMethod.Post, path, new { });
        Assert.Equal(published, await replay.ReadAsync<PublicationStatus>());
        Assert.Equal(2, publisher.Calls);
        Assert.Equal(1, publisher.PrCreates);
        using var taskResponse = await setup.Human.GetAsync(ApiClient.TasksPath(setup.Project) + $"/{setup.Execution.TaskId}");
        Assert.Equal(TaskStage.Review, (await taskResponse.ReadAsync<TaskDto>()).Stage);
        using var foreign = setup.Factory.CreateClient();
        await foreign.RegisterAsync();
        using var denied = await foreign.SendAsync(HttpMethod.Post, path, new { });
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Equal(2, publisher.Calls);
    }
    // model == null keeps the real Codex adapter so a test can point it at the CLI fixture executable.
    private async Task<Scenario> SetupAsync(FixtureModel? model, decimal budget = 5m, IGitPublisher? publisher = null, decimal inputUsd = 1m, decimal outputUsd = 1m)
    {
        var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                {
                    if (model is not null) { services.RemoveAll<ITextModel>(); services.AddSingleton<ITextModel>(model); }
                    if (publisher is not null) { services.RemoveAll<IGitPublisher>(); services.AddSingleton(publisher); }
                }));
        var human = factory.CreateClient();
        await human.RegisterAsync();
        var project = await human.CreateProjectAsync();
        var config = factory.Services.GetRequiredService<IConfiguration>();
        config["Codex:EnableRequests"] = "true"; // Either the explicit double above or the CLI fixture replaces the real CLI.
        var profile = await factory.Services.SeedProfileAsync(project, "Fixture", inputUsd, outputUsd);
        var task = await human.CreateTaskAsync(project);
        var taskPath = ApiClient.TasksPath(project) + $"/{task.Id}";
        using var analysis = await human.SendAsync(HttpMethod.Post, taskPath + "/transition", new TransitionInput(TaskStage.Analysis, task.Version));
        task = await analysis.ReadAsync<TaskDto>();
        using var ready = await human.SendAsync(HttpMethod.Post, taskPath + "/transition", new TransitionInput(TaskStage.Ready, task.Version));
        task = await ready.ReadAsync<TaskDto>();
        using var schedule = await human.SendAsync(HttpMethod.Post, taskPath + "/executions/",
            new ExecutionInput(task.Version, MaxCostUsd: budget, AgentProfileId: profile.Id), Guid.NewGuid().ToString());
        var execution = await schedule.ReadAsync<ExecutionDto>();
        using (var scope = factory.Services.CreateScope())
        {
            var source = new RepositorySnapshot(Guid.NewGuid(), Guid.NewGuid(), 1, Path.GetFullPath("fixture-repository"), "main", new string('a', 40), $"teamflow/{execution.Id:N}", Path.GetFullPath("fixture-repository"));
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Executions.Where(e => e.Id == execution.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.RepositorySnapshot, JsonSerializer.Serialize(source, ApiClient.JsonOptions)));
        }
        using var issued = await human.SendAsync(HttpMethod.Post,
            $"/api/v1/organizations/{project.OrganizationId}/projects/{project.Id}/runners/", new RunnerInput());
        var credential = await issued.ReadAsync<IssuedRunner>();
        var runner = factory.CreateClient();
        runner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential.Token);
        using var claim = await runner.PostAsJsonAsync("/internal/v1/executions/claim", new ClaimInput(120));
        var lease = (await claim.ReadAsync<JsonElement>()).GetProperty("leaseToken").GetGuid();
        var context = new CheckpointContext(1, execution.Id, task.Title + "\n" + task.Description, task.AcceptanceCriteria, new string('a', 40), [], []);
        using var initial = await runner.PutAsJsonAsync($"/internal/v1/executions/{execution.Id}/checkpoint/", new CheckpointInput(lease, context));
        initial.EnsureSuccessStatusCode();
        return new(factory, human, runner, project, execution, lease, context);
    }

    private sealed record Scenario(WebApplicationFactory<Program> Factory, HttpClient Human, HttpClient Runner, ProjectDto Project,
        ExecutionDto Execution, Guid Lease, CheckpointContext Context) : IDisposable
    {
        public string Path => $"/internal/v1/executions/{Execution.Id}";
        public void Dispose() { Runner.Dispose(); Human.Dispose(); Factory.Dispose(); }
    }
    private sealed class FixturePublisher : IGitPublisher
    {
        private PublishedPullRequest? _receipt;
        public int Calls { get; private set; }
        public int PrCreates { get; private set; }
        public Task<PublishedPullRequest> PublishAsync(GitConnection connection, PublicationPlan plan, CancellationToken ct)
        {
            Calls++;
            Assert.Single(plan.Files);
            Assert.Equal("42", plan.Files[0].Content);
            Assert.Contains("Checkpoint:", plan.Body);
            if (_receipt is null)
            {
                PrCreates++;
                _receipt = new(1, $"/api/v1/organizations/{connection.OrganizationId}/projects/{connection.ProjectId}/repository/local/reviews/{plan.ExecutionId}", plan.BaseCommit, new string('b', 40), plan.Branch);
                throw new TeamFlow.Api.Http.ApiException(502, "git_unavailable", "Fixture response lost after effect.");
            }
            return Task.FromResult(_receipt);
        }
    }
    private sealed class FixtureModel : ITextModel
    {
        private int _calls;
        public int Calls => _calls;
        public string Text { get; set; } = "{\"tool\":\"read\",\"path\":\"answer.txt\"}";
        public decimal? Cost { get; init; } = 0.001m;
        public Func<CancellationToken, Task>? BeforeReply { get; init; }
        public async Task<TextModelResult> GenerateAsync(AgentProfile profile, string prompt, decimal budgetUsd, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            Assert.Contains("untrusted data", prompt);
            if (BeforeReply is not null) await BeforeReply(ct);
            return new(Text, Cost is null ? null : 100, Cost is null ? null : 10, Cost);
        }
    }
}
