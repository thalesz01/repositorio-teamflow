using System.Security.Cryptography;
using System.Text;

namespace TeamFlow.Runner;

// Create lists glob patterns for files the agent may add; existing files still need their exact path in Editable.
public sealed record RunnerToolPolicy(string[] Readable, string[] Editable, SandboxCommand Test, string[]? Create = null);
public sealed record RemoteModelReply(Guid OperationId, AgentAction Action, decimal? EstimatedCostUsd, int? InputTokens, int? OutputTokens);
public sealed record RemoteExecutionResult(Guid ExecutionId, Guid OperationId, bool Validated, string Reason, int CheckpointSequence);

public interface IRunnerControl
{
    Task<ExecutionMessage?> ClaimAsync(int leaseSeconds, CancellationToken ct);
    Task<bool> HeartbeatAsync(ExecutionMessage message, int leaseSeconds, CancellationToken ct);
    Task<CheckoutResult> PrepareRepositoryAsync(ExecutionMessage message, ExecutionWorkspace workspace, IRepositoryCheckout checkout, CancellationToken ct);
    Task<RemoteCheckpointReply> SaveCheckpointAsync(ExecutionMessage message, PortableContext context, CancellationToken ct);
    Task<RemoteModelReply> GenerateActionAsync(ExecutionMessage message, PortableContext context, RunnerToolPolicy policy, CancellationToken ct);
    Task<RemoteRecovery> ReadRecoveryAsync(ExecutionMessage message, CancellationToken ct) => Task.FromResult(new RemoteRecovery(null, []));
    Task<RemoteExecutionResult> SaveResultAsync(ExecutionMessage message, HarnessResult result, CancellationToken ct);
}

public static class RunnerOperation
{
    public static Guid Id(ExecutionMessage message, string kind, int sequence) => new(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{message.ExecutionId:N}/{message.Attempt}/{kind}/{sequence}"))[..16]);
}

public sealed class RemoteAgentModel(IRunnerControl control, ExecutionMessage message, RunnerToolPolicy policy) : IAgentModel
{
    public async Task<AgentAction> NextAsync(PortableContext context, CancellationToken ct) =>
        (await control.GenerateActionAsync(message, context, policy, ct)).Action;
}
