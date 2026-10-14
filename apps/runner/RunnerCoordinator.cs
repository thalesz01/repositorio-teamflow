using System.Text.Json;

namespace TeamFlow.Runner;

/// <summary>Tools and sandbox the operator configured for one project folder.</summary>
public sealed record FolderProfile(RunnerToolPolicy Policy, ISandbox Sandbox);

// The operator may configure a profile per folder; the folder comes from the claimed execution, never from the repository's contents.
public sealed class RunnerCoordinator(IRunnerControl control, IRepositoryCheckout checkout, ISandbox sandbox, Func<string?, FolderProfile?>? folderProfile = null)
{
    private static void Validate(RunnerToolPolicy policy)
    {
        if (policy is null || policy.Readable is null || policy.Editable is null || policy.Test is null
            || policy.Test.Arguments is null || string.IsNullOrWhiteSpace(policy.Test.Executable)
            || policy.Test.Timeout < TimeSpan.FromMilliseconds(100) || policy.Test.Timeout > TimeSpan.FromMinutes(5))
            throw new ArgumentException("Invalid controller policy.");
    }

    public async Task<RemoteExecutionResult?> RunOnceAsync(string workspaceRoot, RunnerToolPolicy policy, CancellationToken ct)
    {
        Validate(policy);
        var message = await control.ClaimAsync(120, ct);
        if (message is null) return null;
        var source = message.Repository?.Deserialize<CheckoutSource>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var profile = folderProfile?.Invoke(source?.LocalPath);
        var activeSandbox = sandbox;
        if (profile is not null) { Validate(profile.Policy); policy = profile.Policy; activeSandbox = profile.Sandbox; }
        var context = new PortableContext(1, message.ExecutionId, message.Snapshot.Title + "\n" + message.Snapshot.Description,
            message.Snapshot.AcceptanceCriteria, source?.BaseCommit ?? "", [], [], message.Memory, message.Transfer);
        using var deadline = new CancellationTokenSource(message.DeadlineAt - DateTimeOffset.UtcNow);
        using var running = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        var heartbeat = RenewAsync();
        HarnessResult result;
        try
        {
            if (source is null || message.AgentProfileId is null) throw new InvalidDataException("Execution requires repository and model profile.");
            using var workspace = new ExecutionWorkspace(workspaceRoot);
            var prepared = await control.PrepareRepositoryAsync(message, workspace, checkout, running.Token);
            if (prepared.BaseCommit != source.BaseCommit || prepared.Branch != $"teamflow/{message.ExecutionId:N}")
                throw new InvalidDataException("Checkout does not match the scheduled base or branch.");
            var checkpoints = new TrackingCheckpoint(new RemoteAgentCheckpoint(control, message), c => context = c);
            // An analysis only reads: nothing is editable or creatable and the test command never runs.
            var analysis = message.Snapshot.Kind == "analysis";
            RunnerToolPolicy Effective() => analysis ? ToolPolicyPatterns.Expand(policy, workspace) with { Editable = [], Create = null } : ToolPolicyPatterns.Expand(policy, workspace);
            WorkspaceTools Tools(RunnerToolPolicy value) => new(workspace, activeSandbox, value.Readable, value.Editable, value.Test, value.Create, testsAllowed: !analysis);
            var effective = Effective();
            var tools = Tools(effective);
            await TransferRestore.ApplyAsync(message.Transfer, source.BaseCommit, workspace, tools, running.Token);
            context = await CheckpointRestore.RestoreAsync(context, await control.ReadRecoveryAsync(message, running.Token), workspace, tools, running.Token);
            // Files restored from the parent execution or an earlier attempt are part of the base now. Patterns are expanded again so the
            // model is offered them as ordinary files to read and edit, not only as "new files it may create".
            effective = Effective();
            tools = Tools(effective);
            var harness = new AgentHarness(new RemoteAgentModel(control, message, effective), checkpoints, tools, analysis ? HarnessMode.Analysis : HarnessMode.Change);
            result = await harness.RunAsync(context, message.Limits.MaxSteps,
                message.DeadlineAt - DateTimeOffset.UtcNow, running.Token);
        }
        catch (OperationCanceledException)
        {
            result = new(false, deadline.IsCancellationRequested ? "deadline_exceeded" : ct.IsCancellationRequested ? "runner_failed" : "cancelled", context);
        }
        catch (RunnerProtocolException error)
        {
            result = new(false, error.Code, context);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or ArgumentException or InvalidOperationException or JsonException or InvalidDataException)
        {
            result = new(false, "runner_failed", context);
        }
        finally
        {
            running.Cancel();
            try { await heartbeat; }
            catch (OperationCanceledException) when (running.IsCancellationRequested) { }
        }
        using var report = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        return await control.SaveResultAsync(message, result, report.Token);

        async Task RenewAsync()
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(20), running.Token);
                using var request = CancellationTokenSource.CreateLinkedTokenSource(running.Token);
                request.CancelAfter(TimeSpan.FromSeconds(10));
                try
                {
                    if (await control.HeartbeatAsync(message, 120, request.Token)) continue;
                }
                catch (RunnerProtocolException error)
                {
                    result = new(false, error.Code, context);
                }
                catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
                {
                    if (running.IsCancellationRequested) return;
                }
                running.Cancel();
                return;
            }
        }
    }

    private sealed class TrackingCheckpoint(IAgentCheckpoint remote, Action<PortableContext> saved) : IAgentCheckpoint
    {
        public async Task SaveAsync(PortableContext context, CancellationToken ct)
        {
            await remote.SaveAsync(context, ct);
            saved(context);
        }
    }
}
