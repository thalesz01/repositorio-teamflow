using System.Text.Json;
using TeamFlow.Runner;
using Xunit;

namespace TeamFlow.Runner.Tests;

public sealed class CoordinatorTests
{
    [Fact]
    public async Task CoordinatorClaimsRunsReportsAndRemovesOwnedWorkspaceAsync()
    {
        var control = new FixtureControl();
        var sandbox = new FixtureSandbox();
        var result = await new RunnerCoordinator(control, new FixtureCheckout(), sandbox)
            .RunOnceAsync(Path.GetFullPath("work/coordinator"), Policy(), default);
        Assert.NotNull(result);
        Assert.True(result.Validated);
        Assert.Equal(new[] { 0, 1, 2, 3 }, control.Checkpoints.Select(c => c.Evidence.Length));
        Assert.NotNull(sandbox.WorkspacePath);
        Assert.False(Directory.Exists(sandbox.WorkspacePath));
        Assert.Equal(4, control.ModelCalls);
    }

    [Fact]
    public async Task NoTaskDoesNotCheckoutAndModelRejectionIsReportedWithoutApprovalAsync()
    {
        var empty = new FixtureControl { Empty = true };
        Assert.Null(await new RunnerCoordinator(empty, new FixtureCheckout(), new FixtureSandbox())
            .RunOnceAsync(Path.GetFullPath("work/coordinator"), Policy(), default));
        var rejected = new FixtureControl { ModelFailure = true };
        var result = await new RunnerCoordinator(rejected, new FixtureCheckout(), new FixtureSandbox())
            .RunOnceAsync(Path.GetFullPath("work/coordinator"), Policy(), default);
        Assert.NotNull(result);
        Assert.False(result.Validated);
        Assert.Equal("budget_exceeded", result.Reason);
    }

    [Fact]
    public async Task InvalidDataFromTheCheckoutFailsTheExecutionInsteadOfCrashingTheRunnerAsync()
    {
        var result = await new RunnerCoordinator(new FixtureControl { MismatchedCheckout = true }, new FixtureCheckout(), new FixtureSandbox())
            .RunOnceAsync(Path.GetFullPath("work/coordinator"), Policy(), default);
        Assert.NotNull(result);
        Assert.False(result.Validated);
        Assert.Equal("runner_failed", result.Reason);
    }

    [Fact]
    public async Task WorkerLogsAnUnexpectedFailureAndKeepsServingAsync()
    {
        var logPath = Path.GetFullPath("work/worker-log/runner.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        File.Delete(logPath);
        using var stop = new CancellationTokenSource();
        var control = new FlakyControl(stop);
        var coordinator = new RunnerCoordinator(control, new FixtureCheckout(), new FixtureSandbox());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RunnerWorker(coordinator, new RunnerLog(logPath), TimeSpan.FromMilliseconds(10))
            .RunAsync(Path.GetFullPath("work/coordinator"), Policy(), _ => { }, stop.Token));
        Assert.Equal(2, control.Claims);
        Assert.Contains("Execution loop failed; continuing: InvalidDataException", await File.ReadAllTextAsync(logPath));
    }

    private sealed class FlakyControl(CancellationTokenSource stop) : IRunnerControl
    {
        public int Claims { get; private set; }
        public Task<ExecutionMessage?> ClaimAsync(int leaseSeconds, CancellationToken ct)
        {
            if (++Claims == 1) throw new InvalidDataException("Unexpected claim payload.");
            stop.Cancel();
            return Task.FromResult<ExecutionMessage?>(null);
        }
        public Task<bool> HeartbeatAsync(ExecutionMessage message, int leaseSeconds, CancellationToken ct) => throw new NotSupportedException();
        public Task<CheckoutResult> PrepareRepositoryAsync(ExecutionMessage message, ExecutionWorkspace workspace, IRepositoryCheckout checkout, CancellationToken ct) => throw new NotSupportedException();
        public Task<RemoteCheckpointReply> SaveCheckpointAsync(ExecutionMessage message, PortableContext context, CancellationToken ct) => throw new NotSupportedException();
        public Task<RemoteModelReply> GenerateActionAsync(ExecutionMessage message, PortableContext context, RunnerToolPolicy policy, CancellationToken ct) => throw new NotSupportedException();
        public Task<RemoteExecutionResult> SaveResultAsync(ExecutionMessage message, HarnessResult result, CancellationToken ct) => throw new NotSupportedException();
    }

    [Fact]
    public async Task FilesRestoredFromAParentExecutionAreOfferedAsOrdinaryEditableFilesAsync()
    {
        static string Hash(string value) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
        var baseCommit = new string('a', 40);
        var parentContext = new PortableContext(1, Guid.NewGuid(), "Parent", ["Done"], baseCommit,
            [new(1, "write", "extra.txt", true, "file_created", Hash("from parent")), new(2, "test", null, true, "passed")], []);
        var transfer = new PortableTransfer(parentContext.ExecutionId, baseCommit, new string('b', 40), parentContext, [new(1, "extra.txt", "from parent", "missing", Hash("from parent"))]);
        var control = new FixtureControl { Transfer = JsonSerializer.SerializeToElement(transfer, new JsonSerializerOptions(JsonSerializerDefaults.Web)) };
        // The base checkout only has answer.txt; extra.txt exists only because the parent created it.
        var patterns = new RunnerToolPolicy(["*.txt"], ["*.txt"], new("test", [], TimeSpan.FromSeconds(1)), ["*.txt"]);
        var result = await new RunnerCoordinator(control, new FixtureCheckout(), new FixtureSandbox())
            .RunOnceAsync(Path.GetFullPath("work/coordinator"), patterns, default);
        Assert.NotNull(result);
        Assert.NotNull(control.LastPolicy);
        Assert.Contains("extra.txt", control.LastPolicy.Editable);
        Assert.Contains("extra.txt", control.LastPolicy.Readable);
        Assert.Contains("answer.txt", control.LastPolicy.Editable);
    }

    [Fact]
    public async Task AnalysisRunsReadOnlyWithoutTestsAndReportsAnAnswerAsync()
    {
        var control = new FixtureControl { Analysis = true };
        var sandbox = new FixtureSandbox();
        // The default policy lists answer.txt as editable; an analysis must not inherit that.
        var result = await new RunnerCoordinator(control, new FixtureCheckout(), sandbox)
            .RunOnceAsync(Path.GetFullPath("work/coordinator"), Policy(), default);
        Assert.NotNull(result);
        Assert.True(result.Validated);
        Assert.Equal("analysis_answered", result.Reason);
        Assert.Empty(control.LastPolicy!.Editable);
        Assert.Null(control.LastPolicy.Create);
        Assert.Equal(["answer.txt"], control.LastPolicy.Readable);
        Assert.Null(sandbox.WorkspacePath);
    }

    [Fact]
    public async Task LostLeaseCancelsPendingModelAndReportsCancellationAsync()
    {
        var control = new FixtureControl { WaitForCancellation = true };
        var result = await new RunnerCoordinator(control, new FixtureCheckout(), new FixtureSandbox())
            .RunOnceAsync(Path.GetFullPath("work/coordinator"), Policy(), default);
        Assert.NotNull(result);
        Assert.False(result.Validated);
        Assert.Equal("cancelled", result.Reason);
        Assert.True(control.ModelCancelled);
        Assert.NotNull(control.WorkspacePath);
        Assert.False(Directory.Exists(control.WorkspacePath));
    }
    [Fact]
    public async Task FolderProfileSuppliesToolsAndSandboxOnlyForItsFolderAsync()
    {
        var wrongPolicy = new RunnerToolPolicy(["other.txt"], ["other.txt"], new("test", [], TimeSpan.FromSeconds(1)));
        var folder = Path.GetFullPath("fixture-repo");
        var defaultSandbox = new FixtureSandbox();
        var folderSandbox = new FixtureSandbox();
        var profile = new FolderProfile(Policy(), folderSandbox);
        // Without a matching profile the default policy denies answer.txt, so nothing is approved.
        var unmatched = await new RunnerCoordinator(new FixtureControl(), new FixtureCheckout(), defaultSandbox, path => path == folder + "-other" ? profile : null)
            .RunOnceAsync(Path.GetFullPath("work/coordinator"), wrongPolicy, default);
        Assert.NotNull(unmatched);
        Assert.False(unmatched.Validated);
        Assert.Null(folderSandbox.WorkspacePath);
        // The profile of the claimed execution's folder supplies both the policy and the sandbox.
        var matched = await new RunnerCoordinator(new FixtureControl(), new FixtureCheckout(), defaultSandbox, path => path == folder ? profile : null)
            .RunOnceAsync(Path.GetFullPath("work/coordinator"), wrongPolicy, default);
        Assert.NotNull(matched);
        Assert.True(matched.Validated);
        Assert.NotNull(folderSandbox.WorkspacePath);
        Assert.Null(defaultSandbox.WorkspacePath);
    }

    private static RunnerToolPolicy Policy() => new(["answer.txt"], ["answer.txt"], new("test", [], TimeSpan.FromSeconds(1)));
    private sealed class FixtureControl : IRunnerControl
    {
        private readonly ExecutionMessage _message;
        public bool WaitForCancellation { get; init; }
        public bool ModelCancelled { get; private set; }
        public string? WorkspacePath { get; private set; }
        public bool Empty { get; init; }
        public bool ModelFailure { get; init; }
        public bool MismatchedCheckout { get; init; }
        public JsonElement? Transfer { get; init; }
        public bool Analysis { get; init; }
        public RunnerToolPolicy? LastPolicy { get; private set; }
        public int ModelCalls { get; private set; }
        public List<PortableContext> Checkpoints { get; } = [];
        public FixtureControl()
        {
            var now = DateTimeOffset.UtcNow;
            var id = Guid.NewGuid();
            _message = new(1, id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                now.AddSeconds(120), now.AddMinutes(5), 1, new("Task", "Description", ["Answer equals 42"], Guid.NewGuid()),
                new(900, 3, 10, 5), JsonSerializer.SerializeToElement(new CheckoutSource(Path.GetFullPath("fixture-repo"), new string('a', 40), $"teamflow/{id:N}", Path.GetFullPath("fixture-repo"))), Guid.NewGuid());
        }
        public Task<ExecutionMessage?> ClaimAsync(int leaseSeconds, CancellationToken ct) => Task.FromResult(Empty ? null : Analysis ? _message with { Snapshot = _message.Snapshot with { Kind = "analysis" } } : Transfer is null ? _message : _message with { Transfer = Transfer });
        public Task<bool> HeartbeatAsync(ExecutionMessage message, int leaseSeconds, CancellationToken ct) => Task.FromResult(!WaitForCancellation);
        public Task<CheckoutResult> PrepareRepositoryAsync(ExecutionMessage message, ExecutionWorkspace workspace, IRepositoryCheckout checkout, CancellationToken ct)
        {
            WorkspacePath = workspace.Path;
            if (MismatchedCheckout) return Task.FromResult(new CheckoutResult(new string('b', 40), $"teamflow/{message.ExecutionId:N}"));
            return checkout.PrepareAsync(workspace, new("fixture", new string('a', 40), $"teamflow/{message.ExecutionId:N}"), "fixture", ct);
        }
        public Task<RemoteCheckpointReply> SaveCheckpointAsync(ExecutionMessage message, PortableContext context, CancellationToken ct)
        {
            Checkpoints.Add(context);
            return Task.FromResult(new RemoteCheckpointReply(1, context.Evidence.Length, "fixture", DateTimeOffset.UtcNow, context));
        }
        public async Task<RemoteModelReply> GenerateActionAsync(ExecutionMessage message, PortableContext context, RunnerToolPolicy policy, CancellationToken ct)
        {
            ModelCalls++;
            LastPolicy = policy;
            if (WaitForCancellation)
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
                catch (OperationCanceledException) { ModelCancelled = true; throw; }
            }
            if (ModelFailure) throw new RunnerProtocolException("budget_exceeded", System.Net.HttpStatusCode.Conflict);
            if (Analysis)
                return new RemoteModelReply(Guid.NewGuid(), context.Evidence.Length == 0 ? new AgentAction("read", "answer.txt") : new AgentAction("finish", null, "A regra esta em answer.txt."), 0.001m, 10, 10);
            var action = context.Evidence.Length switch
            {
                0 => new AgentAction("read", "answer.txt"),
                1 => new AgentAction("write", "answer.txt", "42", context.Evidence[0].Hash),
                2 => new AgentAction("test"),
                _ => new AgentAction("finish")
            };
            return new RemoteModelReply(Guid.NewGuid(), action, 0.001m, 10, 10);
        }
        public Task<RemoteExecutionResult> SaveResultAsync(ExecutionMessage message, HarnessResult result, CancellationToken ct)
            => Task.FromResult(new RemoteExecutionResult(message.ExecutionId, Guid.NewGuid(), result.Validated, result.Reason, result.Context.Evidence.Length));
    }
    private sealed class FixtureCheckout : IRepositoryCheckout
    {
        public async Task<CheckoutResult> PrepareAsync(ExecutionWorkspace workspace, CheckoutSource source, string token, CancellationToken ct)
        {
            await File.WriteAllTextAsync(workspace.Resolve("answer.txt"), "41", ct);
            return new(source.BaseCommit, source.Branch);
        }
    }
    private sealed class FixtureSandbox : ISandbox
    {
        public string? WorkspacePath { get; private set; }
        public async Task<SandboxResult> ExecuteAsync(ExecutionWorkspace workspace, SandboxCommand command, CancellationToken ct)
        {
            WorkspacePath = workspace.Path;
            var passed = await File.ReadAllTextAsync(workspace.Resolve("answer.txt"), ct) == "42";
            return new(passed ? 0 : 1, "", "", false, false);
        }
    }
}
