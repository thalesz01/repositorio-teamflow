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
    private static RunnerToolPolicy Policy() => new(["answer.txt"], ["answer.txt"], new("test", [], TimeSpan.FromSeconds(1)));
    private sealed class FixtureControl : IRunnerControl
    {
        private readonly ExecutionMessage _message;
        public bool WaitForCancellation { get; init; }
        public bool ModelCancelled { get; private set; }
        public string? WorkspacePath { get; private set; }
        public bool Empty { get; init; }
        public bool ModelFailure { get; init; }
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
        public Task<ExecutionMessage?> ClaimAsync(int leaseSeconds, CancellationToken ct) => Task.FromResult(Empty ? null : _message);
        public Task<bool> HeartbeatAsync(ExecutionMessage message, int leaseSeconds, CancellationToken ct) => Task.FromResult(!WaitForCancellation);
        public Task<CheckoutResult> PrepareRepositoryAsync(ExecutionMessage message, ExecutionWorkspace workspace, IRepositoryCheckout checkout, CancellationToken ct)
        {
            WorkspacePath = workspace.Path;
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
            if (WaitForCancellation)
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
                catch (OperationCanceledException) { ModelCancelled = true; throw; }
            }
            if (ModelFailure) throw new RunnerProtocolException("budget_exceeded", System.Net.HttpStatusCode.Conflict);
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
