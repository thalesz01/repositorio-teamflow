namespace TeamFlow.Runner;

public sealed class RemoteAgentCheckpoint(IRunnerControl client, ExecutionMessage message) : IAgentCheckpoint
{
    public async Task SaveAsync(PortableContext context, CancellationToken ct)
    {
        await client.SaveCheckpointAsync(message, context, ct);
    }
}
