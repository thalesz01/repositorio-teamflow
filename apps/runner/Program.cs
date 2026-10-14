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
// Folders listed in the controller configuration get their own tools and sandbox image; any other folder uses "policy"/"sandbox".
// The file is read again for each execution, so adding a folder does not require restarting the runner.
var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
var log = new RunnerLog(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? ".", "runner.log"));
var sandboxes = new Dictionary<string, DockerSandbox>();
var hostSandbox = new HostSandbox();
FolderProfile? ProfileFor(string? folder)
{
    if (folder is null) return null;
    var current = config;
    try { current = JsonSerializer.Deserialize<RunnerSettings>(File.ReadAllText(configPath), new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? config; }
    catch (Exception failure) when (failure is IOException or JsonException) { }
    foreach (var (key, entry) in current.Folders ?? [])
    {
        if (!comparer.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(key)), Path.TrimEndingDirectorySeparator(folder))) continue;
        if (string.Equals(entry.Execution, "host", StringComparison.OrdinalIgnoreCase)) return new FolderProfile(entry.Policy, hostSandbox);
        if (entry.Execution is not null && !string.Equals(entry.Execution, "docker", StringComparison.OrdinalIgnoreCase))
            log.Write($"Unknown execution mode '{entry.Execution}' for a folder; using the container sandbox.");
        var limits = entry.Sandbox ?? current.Sandbox;
        var identity = JsonSerializer.Serialize(limits);
        if (!sandboxes.TryGetValue(identity, out var folderSandbox)) sandboxes[identity] = folderSandbox = new DockerSandbox(limits);
        return new FolderProfile(entry.Policy, folderSandbox);
    }
    return null;
}
log.Write($"Runner started in mode {mode}.");
var coordinator = new RunnerCoordinator(client, new GitCheckout(), sandbox, ProfileFor);
if (mode == "run")
{
    try
    {
        await new RunnerWorker(coordinator, log).RunAsync(config.WorkspaceRoot, config.Policy, result =>
        {
            Console.WriteLine(JsonSerializer.Serialize(result));
            log.Write($"Execution {result.ExecutionId} reported: {result.Reason} (validated: {result.Validated}).");
        }, cancellation.Token);
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { log.Write("Runner stopped on request."); }
    catch (Exception error) { log.Failure("Runner stopped unexpectedly", error); throw; }
    return 0;
}
var completed = await coordinator
    .RunOnceAsync(config.WorkspaceRoot, config.Policy, cancellation.Token);
Console.WriteLine(JsonSerializer.Serialize(completed, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
return completed is null || completed.Validated ? 0 : 1;

// Execution "host" runs the test command on this computer instead of in a container (ADR 0013); anything else keeps Docker.
public sealed record FolderSettings(RunnerToolPolicy Policy, SandboxLimits? Sandbox = null, string? Execution = null);
public sealed record RunnerSettings(string Origin, Guid OrganizationId, Guid ProjectId, Guid WorkerId, string WorkspaceRoot, RunnerToolPolicy Policy,
    SandboxLimits? Sandbox = null, Dictionary<string, FolderSettings>? Folders = null);
