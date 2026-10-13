using System.Text.Json;
using TeamFlow.Runner;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
if (args is ["verify-sandbox"])
{
    using var workspace = new ExecutionWorkspace(Path.GetFullPath(Path.Combine("work", "workspaces")));
    var result = await new DockerSandbox().ExecuteAsync(workspace,
        new("/bin/sh", ["-c", "id -u; echo sandbox-ready > result.txt; cat result.txt"], TimeSpan.FromSeconds(20)), cancellation.Token);
    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    return result.ExitCode == 0 && !result.TimedOut ? 0 : 1;
}
if (args is not [var mode, var configPath] || mode is not ("run-once" or "run"))
{
    Console.Error.WriteLine("Usage: dotnet run -- verify-sandbox | (run-once | run) <controller-config.json>");
    return 2;
}
var config = JsonSerializer.Deserialize<RunnerSettings>(await File.ReadAllTextAsync(configPath, cancellation.Token),
    new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidDataException("Missing controller settings.");
var token = Environment.GetEnvironmentVariable("TEAMFLOW_RUNNER_TOKEN") ?? throw new InvalidOperationException("TEAMFLOW_RUNNER_TOKEN is required.");
using var client = new RunnerClient(new Uri(config.Origin), token, config.OrganizationId, config.ProjectId, config.WorkerId);
ExecutionWorkspace.CleanupExpired(config.WorkspaceRoot, DateTimeOffset.UtcNow);
var sandbox = new DockerSandbox(config.Sandbox);
using (var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
    await sandbox.CleanupExpiredAsync(config.WorkspaceRoot, cleanup.Token);
var coordinator = new RunnerCoordinator(client, new GitCheckout(), sandbox);
if (mode == "run")
{
    try
    {
        await new RunnerWorker(coordinator).RunAsync(config.WorkspaceRoot, config.Policy,
        result => Console.WriteLine(JsonSerializer.Serialize(result)), cancellation.Token);
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    return 0;
}
var completed = await coordinator
    .RunOnceAsync(config.WorkspaceRoot, config.Policy, cancellation.Token);
Console.WriteLine(JsonSerializer.Serialize(completed, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
return completed is null || completed.Validated ? 0 : 1;

public sealed record RunnerSettings(string Origin, Guid OrganizationId, Guid ProjectId, Guid WorkerId, string WorkspaceRoot, RunnerToolPolicy Policy, SandboxLimits? Sandbox = null);
