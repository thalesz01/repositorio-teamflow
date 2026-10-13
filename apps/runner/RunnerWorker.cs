namespace TeamFlow.Runner;

public sealed class RunnerWorker(RunnerCoordinator coordinator)
{
    public async Task RunAsync(string workspaceRoot, RunnerToolPolicy policy,
        Action<RemoteExecutionResult> completed, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var result = await coordinator.RunOnceAsync(workspaceRoot, policy, ct);
            if (result is not null) completed(result);
            else await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
    }
}
