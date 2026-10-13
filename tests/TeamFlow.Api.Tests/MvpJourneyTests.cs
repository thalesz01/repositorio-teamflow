using System.Diagnostics;
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
using TeamFlow.Api.Features.Memory;
using TeamFlow.Api.Features.Runners;
using TeamFlow.Api.Features.Tasks;
using R = TeamFlow.Runner;
using Xunit;

namespace TeamFlow.Api.Tests;

public sealed class MvpJourneyTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task DemandExecutionFeedbackMergeAndResolutionReuseRunAcrossNativeModulesAsync()
    {
        using var sourceRepo = new R.ExecutionWorkspace(Path.GetFullPath("work/mvp-source"));
        await GitAsync(sourceRepo.Path, "init", "-b", "main");
        await GitAsync(sourceRepo.Path, "config", "user.name", "TeamFlow Fixture");
        await GitAsync(sourceRepo.Path, "config", "user.email", "fixture@example.test");
        await File.WriteAllTextAsync(sourceRepo.Resolve("answer.txt"), "41");
        await GitAsync(sourceRepo.Path, "add", "answer.txt"); await GitAsync(sourceRepo.Path, "commit", "-m", "fixture base");
        var baseCommit = (await GitAsync(sourceRepo.Path, "rev-parse", "HEAD")).Trim();
        var model = new JourneyModel(); var git = new JourneyGit(baseCommit);
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<ITextModel>(model); services.AddSingleton<IEmbeddingModel>(new JourneyEmbedding());
            services.AddSingleton<IGitPublisher>(git); services.AddSingleton<IGitReview>(git);
        }));
        using var human = factory.CreateClient(); var author = await human.RegisterAsync(); var project = await human.CreateProjectAsync();
        using var reviewer = factory.CreateClient(); var reviewerSession = await reviewer.RegisterAsync();
        using var outsider = factory.CreateClient(); await outsider.RegisterAsync();
        var root = $"/api/v1/organizations/{project.OrganizationId}/projects/{project.Id}";
        var config = factory.Services.GetRequiredService<IConfiguration>();
        var prefix = $"Integrations:{project.OrganizationId}:{project.Id}:";
        config[prefix + "OpenAI:ApiKey"] = "fixture-journey-key";
        config[prefix + "GitHub:CiAppId"] = "100"; config[prefix + "GitHub:RequiredChecks"] = "build";
        // All external boundaries are registered doubles; these flags never enable a real call in this host.
        config["OpenAI:EnableRequests"] = "true"; config["GitHub:EnablePublication"] = "true"; config["GitHub:EnableReview"] = "true";
        config["Memory:EnableEmbeddingRequests"] = "true"; config["Memory:EmbeddingUsdPerMillionTokens"] = "1";
        Guid connectionId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); connectionId = Guid.NewGuid();
            db.Memberships.Add(new() { OrganizationId = project.OrganizationId, UserId = reviewerSession.UserId });
            db.GitConnections.Add(new()
            {
                Id = connectionId,
                Version = Guid.NewGuid(),
                OrganizationId = project.OrganizationId,
                ProjectId = project.Id,
                Owner = "fixture",
                Repository = "repo",
                RepositoryId = 1,
                AppId = 2,
                InstallationId = 3,
                BaseBranch = "main",
                BaseCommit = baseCommit,
                ProtectedPrivateKey = scope.ServiceProvider.GetRequiredService<IntegrationSecrets>().Protect(project.OrganizationId, project.Id, connectionId, "github", "unused-fixture-git-key")
            });
            await db.SaveChangesAsync();
        }
        async Task<ProfileDto> ProfileAsync(string name, string modelName)
        {
            using var response = await human.SendAsync(HttpMethod.Post, root + "/profiles/", new ProfileInput(name, modelName, 65536, 512, 10, 1, 1));
            return await response.ReadAsync<ProfileDto>();
        }
        var originalProfile = await ProfileAsync("Original", "fixture-answer"); var replacementProfile = await ProfileAsync("Replacement", "fixture-newline");
        using var issue = await human.SendAsync(HttpMethod.Post, root + "/runners/", new RunnerInput()); var credential = await issue.ReadAsync<IssuedRunner>();
        using var runner = factory.CreateClient(); runner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential.Token);
        async Task<TaskDto> PlanTaskAsync(string key)
        {
            using var response = await human.SendAsync(HttpMethod.Post, root + "/memory/analyses", new AnalysisInput("answer calculation", "The answer is 42", originalProfile.Id, key));
            var plan = await response.ReadAsync<AnalysisDto>(); Assert.Equal("completed", plan.State); Assert.NotNull(plan.Plan);
            using var accepted = await human.SendAsync(HttpMethod.Post, root + "/tasks/", new TaskInput(plan.Plan.Title, plan.Plan.Description, plan.Plan.AcceptanceCriteria, null, [], plan.Id));
            var task = await accepted.ReadAsync<TaskDto>(); var path = root + $"/tasks/{task.Id}";
            using var analyzing = await human.SendAsync(HttpMethod.Post, path + "/transition", new TransitionInput(TaskStage.Analysis, task.Version)); task = await analyzing.ReadAsync<TaskDto>();
            using var ready = await human.SendAsync(HttpMethod.Post, path + "/transition", new TransitionInput(TaskStage.Ready, task.Version)); return await ready.ReadAsync<TaskDto>();
        }
        async Task<ExecutionDto> ScheduleAsync(TaskDto task, Guid profile)
        {
            using var response = await human.SendAsync(HttpMethod.Post, root + $"/tasks/{task.Id}/executions/", new ExecutionInput(task.Version, AgentProfileId: profile), Guid.NewGuid().ToString());
            return await response.ReadAsync<ExecutionDto>();
        }
        async Task RunAsync(ExecutionDto execution)
        {
            using var claim = await runner.PostAsJsonAsync("/internal/v1/executions/claim", new ClaimInput(120));
            var message = await claim.ReadAsync<R.ExecutionMessage>(); Assert.Equal(execution.Id, message.ExecutionId);
            var repository = message.Repository?.Deserialize<RepositorySnapshot>(ApiClient.JsonOptions); Assert.NotNull(repository);
            using var workspace = new R.ExecutionWorkspace(Path.GetFullPath("work/mvp-run"));
            await GitAsync(sourceRepo.Path, "clone", "--no-hardlinks", sourceRepo.Path, workspace.Path);
            await GitAsync(workspace.Path, "checkout", "--detach", repository.BaseCommit);
            await GitAsync(workspace.Path, "checkout", "-b", repository.Branch);
            var tools = new R.WorkspaceTools(workspace, new R.DockerSandbox(), ["answer.txt"], ["answer.txt"],
                new("/bin/sh", ["-c", "test \"$(cat answer.txt)\" = 42"], TimeSpan.FromSeconds(10)));
            await R.TransferRestore.ApplyAsync(message.Transfer, repository.BaseCommit, workspace, tools, default);
            var initial = new R.PortableContext(1, message.ExecutionId, message.Snapshot.Title + "\n" + message.Snapshot.Description,
                message.Snapshot.AcceptanceCriteria, repository.BaseCommit, [], [], message.Memory, message.Transfer);
            var agent = new HttpAgent(runner, message); var checkpoint = new HttpCheckpoint(runner, message);
            var result = await new R.AgentHarness(agent, checkpoint, tools).RunAsync(initial, 10, TimeSpan.FromSeconds(60), default);
            Assert.True(result.Validated); Assert.Equal("42", (await File.ReadAllTextAsync(workspace.Resolve("answer.txt"))).Trim());
            using var recoveryResponse = await runner.PostAsJsonAsync($"/internal/v1/executions/{execution.Id}/recovery", new RecoveryInput(message.LeaseToken));
            var recovery = await recoveryResponse.ReadAsync<R.RemoteRecovery>(); var calls = model.Calls;
            var restored = await R.CheckpointRestore.RestoreAsync(initial, recovery, workspace, tools, default);
            Assert.Equal(result.Context.Evidence.Length, restored.Evidence.Length); Assert.Equal(calls, model.Calls);
            var terminal = new ExecutionResultInput(message.LeaseToken, R.RunnerOperation.Id(message, "result", restored.Evidence.Length), restored.Evidence.Length, true, "tests_passed");
            using var completed = await runner.PostAsJsonAsync($"/internal/v1/executions/{execution.Id}/result", terminal); completed.EnsureSuccessStatusCode();
            using var replay = await runner.PostAsJsonAsync($"/internal/v1/executions/{execution.Id}/result", terminal); replay.EnsureSuccessStatusCode();
            await GitAsync(workspace.Path, "config", "user.name", "TeamFlow Fixture"); await GitAsync(workspace.Path, "config", "user.email", "fixture@example.test");
            await GitAsync(workspace.Path, "add", "answer.txt"); await GitAsync(workspace.Path, "commit", "-m", "fixture verified execution");
            var head = (await GitAsync(workspace.Path, "rev-parse", "HEAD")).Trim();
            await GitAsync(sourceRepo.Path, "fetch", workspace.Path, $"HEAD:refs/heads/{repository.Branch}");
            git.Heads[execution.Id] = head;
        }
        async Task<PublicationStatus> PublishAsync(ExecutionDto execution)
        {
            var path = root + $"/tasks/{execution.TaskId}/executions/{execution.Id}/publication";
            using var first = await human.SendAsync(HttpMethod.Post, path, new { });
            if (first.StatusCode != HttpStatusCode.OK) Assert.Equal(HttpStatusCode.BadGateway, first.StatusCode);
            using var retry = await human.SendAsync(HttpMethod.Post, path, new { }); var status = await retry.ReadAsync<PublicationStatus>(); Assert.Equal("published", status.State); return status;
        }
        var task = await PlanTaskAsync("journey-original"); var firstExecution = await ScheduleAsync(task, originalProfile.Id);
        await RunAsync(firstExecution); var firstPr = await PublishAsync(firstExecution); Assert.NotNull(firstPr.PullRequest);
        using var currentResponse = await human.GetAsync(root + $"/tasks/{task.Id}"); task = await currentResponse.ReadAsync<TaskDto>(); Assert.Equal(TaskStage.Review, task.Stage);
        var feedback = new ExecutionInput(task.Version, AgentProfileId: replacementProfile.Id, ParentExecutionId: firstExecution.Id, Feedback: "Add a terminal newline while preserving answer 42.");
        using var feedbackResponse = await reviewer.SendAsync(HttpMethod.Post, root + $"/tasks/{task.Id}/executions/", feedback, "journey-feedback"); var secondExecution = await feedbackResponse.ReadAsync<ExecutionDto>();
        using var feedbackReplay = await reviewer.SendAsync(HttpMethod.Post, root + $"/tasks/{task.Id}/executions/", feedback, "journey-feedback"); Assert.Equal(secondExecution.Id, (await feedbackReplay.ReadAsync<ExecutionDto>()).Id);
        await RunAsync(secondExecution); var secondPr = await PublishAsync(secondExecution); Assert.NotNull(secondPr.PullRequest); Assert.NotEqual(firstPr.PullRequest.Number, secondPr.PullRequest.Number);
        var reviewPath = root + $"/tasks/{task.Id}/executions/{secondExecution.Id}/review";
        using var ownApproval = await reviewer.SendAsync(HttpMethod.Post, reviewPath + "/approve", new { }); Assert.Equal(HttpStatusCode.Conflict, ownApproval.StatusCode);
        using var approval = await human.SendAsync(HttpMethod.Post, reviewPath + "/approve", new { }); Assert.Equal("awaiting_merge", (await approval.ReadAsync<ReviewStatus>()).State);
        using var beforeMerge = await human.GetAsync(root + $"/tasks/{task.Id}"); Assert.Equal(TaskStage.AwaitingMerge, (await beforeMerge.ReadAsync<TaskDto>()).Stage);
        await GitAsync(sourceRepo.Path, "merge", "--ff-only", git.Heads[secondExecution.Id]);
        git.MergedNumber = secondPr.PullRequest.Number; git.MergeCommit = git.Heads[secondExecution.Id];
        using var merged = await human.SendAsync(HttpMethod.Post, reviewPath + "/refresh", new { }); Assert.Equal("merged", (await merged.ReadAsync<ReviewStatus>()).State);
        using var done = await human.GetAsync(root + $"/tasks/{task.Id}"); Assert.Equal(TaskStage.Done, (await done.ReadAsync<TaskDto>()).Stage);
        using var memoriesResponse = await human.GetAsync(root + "/memory/?page=1&pageSize=20");
        var sources = await memoriesResponse.ReadAsync<TeamFlow.Api.Http.PageResult<MemoryDto>>(); var resolution = Assert.Single(sources.Items); Assert.Equal(MemoryStatus.Candidate, resolution.Status); Assert.Equal(git.MergeCommit, resolution.Commit);
        using var detailResponse = await human.GetAsync(root + $"/memory/{resolution.Id}"); var detail = await detailResponse.ReadAsync<MemoryDetail>();
        using var validatedResponse = await human.SendAsync(HttpMethod.Put, root + $"/memory/{resolution.Id}", new MemoryUpdate(resolution.Version, resolution.Title, detail.Content, MemoryStatus.Validated)); detail = await validatedResponse.ReadAsync<MemoryDetail>();
        using var indexResponse = await human.SendAsync(HttpMethod.Post, root + $"/memory/{resolution.Id}/indexes", new IndexInput(detail.Item.Version, originalProfile.Id, "journey-index", 1)); indexResponse.EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<MemoryIndexAccess>().ProcessAsync(default));
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().GitConnections.Where(g => g.Id == connectionId)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.BaseCommit, git.MergeCommit).SetProperty(g => g.Version, Guid.NewGuid()));
        }
        model.ExpectedSourceId = resolution.Id;
        var reuseTask = await PlanTaskAsync("journey-reuse"); var reuseExecution = await ScheduleAsync(reuseTask, originalProfile.Id); await RunAsync(reuseExecution);
        Assert.True(model.SawSourceDuringPlanning); Assert.True(model.SawSourceDuringExecution);
        using var foreign = await outsider.SendAsync(HttpMethod.Post, root + "/memory/search", new SearchInput("answer")); Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(2, git.PrCreates); Assert.Equal(3, git.Heads.Count);
        Assert.NotEqual(author.UserId, reviewerSession.UserId);
    }

    private static async Task<string> GitAsync(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Git did not start.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token); var error = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token); Assert.Equal(0, process.ExitCode); await error; return await output;
    }
    private sealed class HttpCheckpoint(HttpClient client, R.ExecutionMessage message) : R.IAgentCheckpoint
    {
        public async Task SaveAsync(R.PortableContext context, CancellationToken ct)
        { using var response = await client.PutAsJsonAsync($"/internal/v1/executions/{message.ExecutionId}/checkpoint/", new { leaseToken = message.LeaseToken, context }, ct); response.EnsureSuccessStatusCode(); }
    }
    private sealed class HttpAgent(HttpClient client, R.ExecutionMessage message) : R.IAgentModel
    {
        public async Task<R.AgentAction> NextAsync(R.PortableContext context, CancellationToken ct)
        {
            using var response = await client.PostAsJsonAsync($"/internal/v1/executions/{message.ExecutionId}/model-turn", new ModelTurnInput(message.LeaseToken,
                R.RunnerOperation.Id(message, "model", context.Evidence.Length), context.Evidence.Length, new(["answer.txt"], ["answer.txt"])), ct);
            var action = (await response.ReadAsync<ModelTurnReply>()).Action; return new(action.Tool, action.Path, action.Content, action.ExpectedHash);
        }
    }
    private sealed class JourneyModel : ITextModel
    {
        public int Calls { get; private set; }
        public Guid? ExpectedSourceId { get; set; }
        public bool SawSourceDuringPlanning { get; private set; }
        public bool SawSourceDuringExecution { get; private set; }
        public Task<TextModelResult> GenerateAsync(AgentProfile profile, string key, string prompt, decimal budget, CancellationToken ct)
        {
            Calls++; Assert.Contains("untrusted data", prompt); Assert.DoesNotContain(key, prompt);
            if (prompt.StartsWith("Propose", StringComparison.Ordinal))
            {
                if (ExpectedSourceId is { } source) { Assert.Contains(source.ToString(), prompt); SawSourceDuringPlanning = true; }
                return Task.FromResult(new TextModelResult(JsonSerializer.Serialize(new PlanDraft("Answer calculation", "Set the answer to 42.", ["Answer equals 42"], []), ApiClient.JsonOptions), 100, 100, 0.001m));
            }
            var marker = prompt.IndexOf("\nContext: ", StringComparison.Ordinal); Assert.True(marker > 0);
            var context = JsonSerializer.Deserialize<CheckpointContext>(prompt[(marker + 10)..], ApiClient.JsonOptions); Assert.NotNull(context);
            if (ExpectedSourceId is { } id) { Assert.Contains(id.ToString(), context.Memory?.GetRawText() ?? ""); SawSourceDuringExecution = true; }
            var action = context.Evidence.Length switch
            {
                0 => new ModelAction("read", "answer.txt"),
                1 => new ModelAction("write", "answer.txt", profile.Model == "fixture-newline" ? "42\n" : "42", context.Evidence[0].Hash),
                2 => new ModelAction("test"),
                _ => new ModelAction("finish")
            };
            return Task.FromResult(new TextModelResult(JsonSerializer.Serialize(action, ApiClient.JsonOptions), 100, 100, 0.001m));
        }
    }
    private sealed class JourneyEmbedding : IEmbeddingModel
    {
        public Task<EmbeddingResult> EmbedAsync(string key, string[] inputs, decimal price, CancellationToken ct) =>
            Task.FromResult(new EmbeddingResult(inputs.Select(_ => Enumerable.Repeat(0.1f, 1536).ToArray()).ToArray(), 100, 0.0001m));
    }
    private sealed class JourneyGit(string baseCommit) : IGitPublisher, IGitReview
    {
        private readonly Dictionary<Guid, PublishedPullRequest> _receipts = [];
        public Dictionary<Guid, string> Heads { get; } = [];
        public int PrCreates { get; private set; }
        public long? MergedNumber { get; set; }
        public string? MergeCommit { get; set; }
        public Task<PublishedPullRequest> PublishAsync(GitConnection connection, string key, PublicationPlan plan, CancellationToken ct)
        {
            Assert.Single(plan.Files); Assert.Equal("42", plan.Files[0].Content.Trim()); Assert.DoesNotContain(key, plan.Body);
            if (_receipts.TryGetValue(plan.ExecutionId, out var receipt)) return Task.FromResult(receipt);
            PrCreates++; receipt = new(PrCreates, $"https://github.com/fixture/repo/pull/{PrCreates}", plan.BaseCommit, Heads[plan.ExecutionId], plan.Branch);
            _receipts.Add(plan.ExecutionId, receipt);
            throw new TeamFlow.Api.Http.ApiException(502, "git_unavailable", "Simulated response lost after PR creation.");
        }
        public Task<GitReviewSnapshot> ReadAsync(GitConnection connection, string key, long number, string branch, CiPolicy policy, CancellationToken ct)
        {
            var receipt = _receipts.Values.Single(r => r.Number == number); Assert.Equal(receipt.Branch, branch);
            var merged = MergedNumber == number;
            return Task.FromResult(new GitReviewSnapshot(receipt.HeadCommit, merged ? MergeCommit ?? baseCommit : baseCommit, true, merged, merged ? MergeCommit : null, merged ? baseCommit : null));
        }
    }
}
