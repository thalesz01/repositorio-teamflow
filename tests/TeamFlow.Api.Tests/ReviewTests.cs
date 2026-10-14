using System.Security.Cryptography;
using System.Text;
using System.Net;
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
using TeamFlow.Api.Features.Organizations;
using TeamFlow.Api.Features.Projects;
using TeamFlow.Api.Features.Tasks;
using Xunit;

namespace TeamFlow.Api.Tests;

public sealed class ReviewTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task IndependentApprovalDoesNotFinishUntilVerifiedMergeAndReplayIsStableAsync()
    {
        using var setup = await SetupAsync();
        using var own = await setup.Author.SendAsync(HttpMethod.Post, setup.Path + "/approve", new { });
        Assert.Equal(HttpStatusCode.Conflict, own.StatusCode);
        using var approval = await setup.Reviewer.SendAsync(HttpMethod.Post, setup.Path + "/approve", new { });
        Assert.Equal("awaiting_merge", (await approval.ReadAsync<ReviewStatus>()).State);
        Assert.Equal(TaskStage.AwaitingMerge, await StageAsync(setup));
        setup.Provider.Snapshot = setup.Provider.Snapshot with { Merged = true, MergeCommit = new string('d', 40), MergeBaseParent = new string('a', 40) };
        using var merged = await setup.Author.SendAsync(HttpMethod.Post, setup.Path + "/refresh", new { });
        Assert.Equal("merged", (await merged.ReadAsync<ReviewStatus>()).State);
        Assert.Equal(TaskStage.Done, await StageAsync(setup));
        using (var memoryScope = setup.Factory.Services.CreateScope())
        {
            var item = Assert.Single(await memoryScope.ServiceProvider.GetRequiredService<AppDbContext>().MemoryItems.Where(m => m.TaskId == setup.TaskId).ToListAsync());
            Assert.Equal(TeamFlow.Api.Features.Memory.MemoryStatus.Candidate, item.Status);
            Assert.Equal(new string('d', 40), item.Commit);
            Assert.Contains("/repository/local/reviews/", item.PullRequestUrl);
        }
        setup.Provider.Snapshot = setup.Provider.Snapshot with { Merged = false, CiPassed = false };
        using var replay = await setup.Author.SendAsync(HttpMethod.Post, setup.Path + "/refresh", new { });
        Assert.Equal("merged", (await replay.ReadAsync<ReviewStatus>()).State);
        Assert.Equal(TaskStage.Done, await StageAsync(setup));
    }

    [Fact]
    public async Task HeadOrBaseChangesInvalidateApprovalAndUnverifiedHeadCannotApproveAsync()
    {
        using var setup = await SetupAsync();
        setup.Provider.Snapshot = setup.Provider.Snapshot with { CiPassed = false };
        using var failedCi = await setup.Reviewer.SendAsync(HttpMethod.Post, setup.Path + "/approve", new { });
        Assert.Equal(HttpStatusCode.Conflict, failedCi.StatusCode);
        setup.Provider.Snapshot = setup.Provider.Snapshot with { CiPassed = true };
        using var approval = await setup.Reviewer.SendAsync(HttpMethod.Post, setup.Path + "/approve", new { });
        approval.EnsureSuccessStatusCode();
        setup.Provider.Snapshot = setup.Provider.Snapshot with { BaseCommit = new string('c', 40) };
        using var changed = await setup.Author.SendAsync(HttpMethod.Post, setup.Path + "/refresh", new { });
        var status = await changed.ReadAsync<ReviewStatus>();
        Assert.Equal("revalidation_required", status.State);
        Assert.Null(status.ApprovedBy);
        Assert.Equal(TaskStage.Review, await StageAsync(setup));
        setup.Provider.Snapshot = setup.Provider.Snapshot with { BaseCommit = new string('a', 40), HeadCommit = new string('c', 40) };
        using var changedHead = await setup.Author.SendAsync(HttpMethod.Post, setup.Path + "/refresh", new { });
        Assert.Equal("revalidation_required", (await changedHead.ReadAsync<ReviewStatus>()).State);
    }

    [Fact]
    public async Task MergeWithoutApprovalAndForeignTenantCannotCompleteTaskAsync()
    {
        using var setup = await SetupAsync();
        setup.Provider.Snapshot = setup.Provider.Snapshot with { Merged = true, MergeCommit = new string('d', 40), MergeBaseParent = new string('a', 40) };
        using var merge = await setup.Author.SendAsync(HttpMethod.Post, setup.Path + "/refresh", new { });
        Assert.Equal("merge_policy_unverified", (await merge.ReadAsync<ReviewStatus>()).State);
        Assert.Equal(TaskStage.Review, await StageAsync(setup));
        using var foreign = setup.Factory.CreateClient();
        await foreign.RegisterAsync();
        using var denied = await foreign.SendAsync(HttpMethod.Post, setup.Path + "/approve", new { });
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
    }

    [Fact]
    public async Task FeedbackCreatesOneLinkedExecutionAndOldReviewCannotFinishItAsync()
    {
        using var setup = await SetupAsync();
        var taskPath = setup.Path[..setup.Path.IndexOf("/executions/", StringComparison.Ordinal)];
        using var current = await setup.Author.GetAsync(taskPath);
        var task = await current.ReadAsync<TaskDto>();
        var parentId = Guid.Parse(setup.Path.Split('/')[^2]);
        var replacement = await setup.Factory.Services.SeedProfileAsync(setup.Project, "Replacement agent");
        var input = new ExecutionInput(task.Version, AgentProfileId: replacement.Id, ParentExecutionId: parentId, Feedback: "Add the negative-value case.");
        var key = Guid.NewGuid().ToString();
        using var created = await setup.Reviewer.SendAsync(HttpMethod.Post, taskPath + "/executions/", input, key);
        var execution = await created.ReadAsync<ExecutionDto>();
        using var replay = await setup.Reviewer.SendAsync(HttpMethod.Post, taskPath + "/executions/", input, key);
        Assert.Equal(execution.Id, (await replay.ReadAsync<ExecutionDto>()).Id);
        using var scope = setup.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var linked = await db.Executions.SingleAsync(e => e.Id == execution.Id);
        Assert.Equal(parentId, linked.ParentExecutionId);
        Assert.Equal(replacement.Id, linked.AgentProfileId);
        var portable = JsonSerializer.Deserialize<PortableTransfer>(linked.TransferSnapshot ?? throw new InvalidDataException("Missing portable transfer."), ApiClient.JsonOptions);
        Assert.NotNull(portable); Assert.Equal(parentId, portable.ParentExecutionId); Assert.Equal(new string('a', 40), portable.BaseCommit);
        Assert.Equal("42", Assert.Single(portable.Writes).Content); Assert.Contains(portable.Context.Evidence, e => e.Tool == "test" && e.Succeeded);
        var changed = JsonSerializer.Deserialize<RepositorySnapshot>(linked.RepositorySnapshot ?? "null", ApiClient.JsonOptions);
        Assert.NotNull(changed);
        var parent = await db.Executions.AsNoTracking().SingleAsync(e => e.Id == parentId);
        var conflict = await Assert.ThrowsAsync<TeamFlow.Api.Http.ApiException>(() => scope.ServiceProvider.GetRequiredService<ExecutionTransfer>()
            .BuildAsync(parent, JsonSerializer.Serialize(changed with { BaseCommit = new string('c', 40) }, ApiClient.JsonOptions), default));
        Assert.Equal("transfer_base_changed", conflict.Code);
        Assert.Equal("Add the negative-value case.", linked.ReviewFeedback);
        Assert.Contains("negative-value", linked.TaskSnapshot);
        Assert.Equal(2, await db.Executions.CountAsync(e => e.TaskId == setup.TaskId));
        using var staleApproval = await setup.Reviewer.SendAsync(HttpMethod.Post, setup.Path + "/approve", new { });
        Assert.Equal(HttpStatusCode.Conflict, staleApproval.StatusCode);
        Assert.Equal(TaskStage.Execution, await StageAsync(setup));
    }

    [Fact]
    public async Task DelayedGitQueryCannotOverwriteNewerVerificationStateAsync()
    {
        using var setup = await SetupAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        setup.Provider.BeforeReply = async ct =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                started.TrySetResult();
                await release.Task.WaitAsync(ct);
            }
        };
        var delayed = setup.Author.SendAsync(HttpMethod.Post, setup.Path + "/refresh", new { });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            setup.Provider.Snapshot = setup.Provider.Snapshot with { CiPassed = false };
            using var newer = await setup.Reviewer.SendAsync(HttpMethod.Post, setup.Path + "/refresh", new { });
            Assert.Equal("ci_pending", (await newer.ReadAsync<ReviewStatus>()).State);
        }
        finally { release.TrySetResult(); }
        using var stale = await delayed;
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var state = await setup.Author.GetAsync(setup.Path);
        Assert.Equal("ci_pending", (await state.ReadAsync<ReviewStatus>()).State);
    }
    [Fact]
    public async Task LocalReviewUsesNativeGitAndRequiresApprovalBeforeConfirmedFastForwardAsync()
    {
        using var setup = await SetupAsync(realGit: true);
        using var workspace = new TeamFlow.Runner.ExecutionWorkspace(Path.GetFullPath(Path.Combine("work", "local-review")));
        await GitAsync(["init", "-b", "main"]);
        await GitAsync(["-c", "user.name=Fixture", "-c", "user.email=fixture@example.test", "commit", "--allow-empty", "-m", "Base"]);
        var baseline = (await GitAsync(["rev-parse", "HEAD"])).Trim();
        await PublishLocallyAsync(setup, workspace.Path, baseline);
        using var own = await setup.Author.SendAsync(HttpMethod.Post, setup.Path + "/approve", new { });
        Assert.Equal(HttpStatusCode.Conflict, own.StatusCode);
        using var approved = await setup.Reviewer.SendAsync(HttpMethod.Post, setup.Path + "/approve", new { });
        Assert.Equal("awaiting_merge", (await approved.ReadAsync<ReviewStatus>()).State);
        Assert.Equal(TaskStage.AwaitingMerge, await StageAsync(setup));
        using (var scope = setup.Factory.Services.CreateScope())
        {
            var execution = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Executions.SingleAsync(e => e.TaskId == setup.TaskId);
            var source = JsonSerializer.Deserialize<RepositorySnapshot>(execution.RepositorySnapshot!, ApiClient.JsonOptions)!;
            await GitAsync(["merge", "--ff-only", source.Branch]);
        }
        using var refreshed = await setup.Author.SendAsync(HttpMethod.Post, setup.Path + "/refresh", new { });
        Assert.Equal("merged", (await refreshed.ReadAsync<ReviewStatus>()).State); Assert.Equal(TaskStage.Done, await StageAsync(setup));

        async Task<string> GitAsync(string[] arguments)
        {
            var start = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = workspace.Path, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(); var output = await stdout; await stderr; Assert.Equal(0, process.ExitCode); return output;
        }
    }

    // Publishes the scenario's execution with the real LocalGit into a repository owned by the test.
    private static async Task PublishLocallyAsync(Scenario setup, string workspacePath, string baseline)
    {
        using (var scope = setup.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var execution = await db.Executions.SingleAsync(e => e.TaskId == setup.TaskId);
            var original = JsonSerializer.Deserialize<RepositorySnapshot>(execution.RepositorySnapshot!, ApiClient.JsonOptions)!;
            var connection = await db.GitConnections.SingleAsync(g => g.Id == original.ConnectionId);
            connection.Provider = "local"; connection.LocalPath = workspacePath; connection.BaseCommit = baseline;
            var config = setup.Factory.Services.GetRequiredService<IConfiguration>();
            config[$"Integrations:{connection.OrganizationId:D}:{connection.ProjectId:D}:Local:RepositoryPath"] = workspacePath;
            var content = "42"; var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
            var plan = new PublicationPlan(execution.Id, baseline, original.Branch, "Local fixture", "Verified fixture tests", execution.CreatedAt, [new("answer.txt", content, hash)]);
            var publication = await scope.ServiceProvider.GetRequiredService<LocalGit>().PublishAsync(connection, plan, CancellationToken.None);
            execution.RepositorySnapshot = JsonSerializer.Serialize(original with { LocalPath = workspacePath, RemoteUrl = workspacePath, BaseCommit = baseline }, ApiClient.JsonOptions);
            execution.PublishedHeadCommit = publication.HeadCommit; execution.PullRequestUrl = publication.Url;
            await db.SaveChangesAsync();
        }
    }

    private static async Task<string> RunGitAsync(string directory, params string[] arguments)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(); var output = await stdout; await stderr; Assert.Equal(0, process.ExitCode); return output;
    }

    [Fact]
    public async Task MergeFromTheAppNeedsTheFlagAndAnIndependentApprovalThenMovesTheWorkingTreeAsync()
    {
        using var setup = await SetupAsync(realGit: true);
        using var workspace = new TeamFlow.Runner.ExecutionWorkspace(Path.GetFullPath(Path.Combine("work", "local-merge")));
        await RunGitAsync(workspace.Path, "init", "-b", "main");
        await RunGitAsync(workspace.Path, "-c", "user.name=Fixture", "-c", "user.email=fixture@example.test", "commit", "--allow-empty", "-m", "Base");
        var baseline = (await RunGitAsync(workspace.Path, "rev-parse", "HEAD")).Trim();
        await PublishLocallyAsync(setup, workspace.Path, baseline);
        var mergePath = setup.Path + "/merge";
        using var disabled = await setup.Reviewer.SendAsync(HttpMethod.Post, mergePath, new { });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, disabled.StatusCode);
        Assert.Equal("merge_disabled", (await disabled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        setup.Factory.Services.GetRequiredService<IConfiguration>()["Local:EnableMerge"] = "true";
        using var early = await setup.Reviewer.SendAsync(HttpMethod.Post, mergePath, new { });
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Equal("approval_required", (await early.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.False(File.Exists(Path.Combine(workspace.Path, "answer.txt")));
        using var approved = await setup.Reviewer.SendAsync(HttpMethod.Post, setup.Path + "/approve", new { });
        var approval = await approved.ReadAsync<ReviewStatus>();
        Assert.Equal("awaiting_merge", approval.State);
        Assert.True(approval.MergeAvailable);
        using var merged = await setup.Author.SendAsync(HttpMethod.Post, mergePath, new { });
        var result = await merged.ReadAsync<ReviewStatus>();
        Assert.Equal("merged", result.State);
        Assert.False(result.MergeAvailable);
        Assert.Equal(TaskStage.Done, await StageAsync(setup));
        Assert.Equal("42", await File.ReadAllTextAsync(Path.Combine(workspace.Path, "answer.txt")));
        Assert.Equal(result.MergeCommit, (await RunGitAsync(workspace.Path, "rev-parse", "main")).Trim());
        using (var scope = setup.Factory.Services.CreateScope())
        {
            var execution = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Executions.SingleAsync(e => e.TaskId == setup.TaskId);
            Assert.NotNull(execution.MergeRequestedBy);
            Assert.NotNull(execution.MergeRequestedAt);
        }
        using var replay = await setup.Author.SendAsync(HttpMethod.Post, mergePath, new { });
        Assert.Equal("merged", (await replay.ReadAsync<ReviewStatus>()).State);
    }

    [Fact]
    public async Task CompleteApprovesAndMergesInOneStepOnlyForSingleUserAsync()
    {
        using var setup = await SetupAsync(realGit: true);
        using var workspace = new TeamFlow.Runner.ExecutionWorkspace(Path.GetFullPath(Path.Combine("work", "local-complete")));
        await RunGitAsync(workspace.Path, "init", "-b", "main");
        await RunGitAsync(workspace.Path, "-c", "user.name=Fixture", "-c", "user.email=fixture@example.test", "commit", "--allow-empty", "-m", "Base");
        var baseline = (await RunGitAsync(workspace.Path, "rev-parse", "HEAD")).Trim();
        await PublishLocallyAsync(setup, workspace.Path, baseline);
        var configuration = setup.Factory.Services.GetRequiredService<IConfiguration>();
        configuration["Local:EnableMerge"] = "true";
        var completePath = setup.Path + "/complete";
        using var own = await setup.Author.SendAsync(HttpMethod.Post, completePath, new { });
        Assert.Equal(HttpStatusCode.Conflict, own.StatusCode);
        Assert.Equal("independent_review_required", (await own.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.False(File.Exists(Path.Combine(workspace.Path, "answer.txt")));
        configuration["Local:SingleUser"] = "true";
        using var completed = await setup.Author.SendAsync(HttpMethod.Post, completePath, new { });
        var result = await completed.ReadAsync<ReviewStatus>();
        Assert.Equal("merged", result.State);
        Assert.Equal(TaskStage.Done, await StageAsync(setup));
        Assert.Equal("42", await File.ReadAllTextAsync(Path.Combine(workspace.Path, "answer.txt")));
        using var replay = await setup.Author.SendAsync(HttpMethod.Post, completePath, new { });
        Assert.Equal("merged", (await replay.ReadAsync<ReviewStatus>()).State);
    }

    private static async Task<TaskStage> StageAsync(Scenario setup)
    {
        using var scope = setup.Factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<AppDbContext>().WorkItems.SingleAsync(t => t.Id == setup.TaskId)).Stage;
    }

    // realGit keeps the native Git review; otherwise the explicit double supplies the head, base and merge facts.
    private async Task<Scenario> SetupAsync(bool realGit = false)
    {
        var provider = new FixtureReview();
        var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        { if (!realGit) { services.RemoveAll<IGitReview>(); services.AddSingleton<IGitReview>(provider); } }));
        var author = factory.CreateClient();
        var requestedBy = (await author.RegisterAsync()).UserId;
        var project = await author.CreateProjectAsync();
        var task = await author.CreateTaskAsync(project);
        var profile = await factory.Services.SeedProfileAsync(project, "Original agent");
        var reviewer = factory.CreateClient();
        var reviewerId = (await reviewer.RegisterAsync()).UserId;
        var id = Guid.NewGuid();
        var connection = Guid.NewGuid();
        var version = Guid.NewGuid();
        var repositoryPath = Path.GetFullPath("fixture-repository");
        var source = new RepositorySnapshot(connection, version, 1, repositoryPath, "main", new string('a', 40), $"teamflow/{id:N}", repositoryPath);
        factory.Services.GetRequiredService<IConfiguration>()["Local:EnableReview"] = "true"; // Review reads the explicit double or a test-owned repository.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Memberships.Add(new() { OrganizationId = project.OrganizationId, UserId = reviewerId });
            db.GitConnections.Add(new()
            {
                Id = connection,
                Version = version,
                OrganizationId = project.OrganizationId,
                ProjectId = project.Id,
                Provider = "local",
                LocalPath = repositoryPath,
                RepositoryId = 1,
                BaseBranch = "main",
                BaseCommit = source.BaseCommit
            });
            db.Executions.Add(new()
            {
                Id = id,
                OrganizationId = project.OrganizationId,
                ProjectId = project.Id,
                TaskId = task.Id,
                RequestedBy = requestedBy,
                IdempotencyKey = id.ToString(),
                RequestHash = new string('a', 64),
                TaskSnapshot = JsonSerializer.Serialize(task, ApiClient.JsonOptions),
                AgentProfileId = profile.Id,
                Attempt = 1,
                ResultCheckpointSequence = 2,
                RepositorySnapshot = JsonSerializer.Serialize(source, ApiClient.JsonOptions),
                State = ExecutionState.Succeeded,
                ResultCode = "tests_passed",
                PublicationState = "published",
                PullRequestNumber = 7,
                PullRequestUrl = $"/api/v1/organizations/{project.OrganizationId}/projects/{project.Id}/repository/local/reviews/{id}",
                PublishedHeadCommit = new string('b', 40),
                CreatedAt = fixture.Clock.GetUtcNow()
            });
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("42")));
            var original = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("41")));
            var context = new CheckpointContext(1, id, task.Title + "\n" + task.Description, task.AcceptanceCriteria, source.BaseCommit,
                [new(1, "write", "answer.txt", true, "file_updated", hash), new(2, "test", null, true, "fixture-test-passed")], []);
            db.ExecutionCheckpoints.Add(new()
            {
                ExecutionId = id,
                OrganizationId = project.OrganizationId,
                ProjectId = project.Id,
                Attempt = 1,
                Sequence = 2,
                Context = JsonSerializer.Serialize(context, ApiClient.JsonOptions),
                ContentHash = new string('a', 64),
                CreatedAt = fixture.Clock.GetUtcNow()
            });
            db.ModelTurns.Add(new()
            {
                ExecutionId = id,
                OperationId = Guid.NewGuid(),
                OrganizationId = project.OrganizationId,
                ProjectId = project.Id,
                Attempt = 1,
                Sequence = 0,
                State = ModelTurnState.Completed,
                EstimatedCostUsd = 0.01m,
                Action = JsonSerializer.Serialize(new ModelAction("write", "answer.txt", "42", original), ApiClient.JsonOptions)
            });
            await db.WorkItems.Where(t => t.Id == task.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Stage, TaskStage.Review));
            await db.SaveChangesAsync();
        }
        return new(factory, author, reviewer, provider, project, task.Id,
            ApiClient.TasksPath(project) + $"/{task.Id}/executions/{id}/review");
    }
    private sealed class FixtureReview : IGitReview
    {
        public Func<CancellationToken, Task>? BeforeReply { get; set; }
        public GitReviewSnapshot Snapshot { get; set; } = new(new string('b', 40), new string('a', 40), true, false, null, null);
        public async Task<GitReviewSnapshot> ReadAsync(GitConnection connection, RepositorySnapshot snapshot, string expectedHead, CancellationToken ct)
        {
            Assert.Equal(new string('b', 40), expectedHead);
            var captured = Snapshot;
            if (BeforeReply is not null) await BeforeReply(ct);
            return captured;
        }
    }
    private sealed record Scenario(WebApplicationFactory<Program> Factory, HttpClient Author, HttpClient Reviewer, FixtureReview Provider,
        ProjectDto Project, Guid TaskId, string Path) : IDisposable
    {
        public void Dispose() { Author.Dispose(); Reviewer.Dispose(); Factory.Dispose(); }
    }
}
