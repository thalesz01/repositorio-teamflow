namespace TeamFlow.Runner;

public sealed class RunnerWorker(RunnerCoordinator coordinator, RunnerLog? log = null, TimeSpan? retryDelay = null)
{
    public async Task RunAsync(string workspaceRoot, RunnerToolPolicy policy,
        Action<RemoteExecutionResult> completed, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await coordinator.RunOnceAsync(workspaceRoot, policy, ct);
                if (result is not null) completed(result);
                else await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
            // An unexpected failure must not end the process: it would drop the lease silently and repeat on the next start.
            catch (Exception error) when (error is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                log?.Failure("Execution loop failed; continuing", error);
                await Task.Delay(retryDelay ?? TimeSpan.FromSeconds(5), ct);
            }
        }
    }
}
