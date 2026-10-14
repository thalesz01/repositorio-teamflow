using System.Diagnostics;

namespace TeamFlow.Api.Features.Runners;

/// <summary>Development-only process supervision; the runner remains an independent HTTP client.</summary>
public sealed class LocalRunnerSupervisor(IConfiguration configuration, IWebHostEnvironment environment,
    IHostApplicationLifetime lifetime, ILogger<LocalRunnerSupervisor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!environment.IsDevelopment() || !OperatingSystem.IsWindows()
            || !configuration.GetValue<bool>("Local:AutoStartRunners")) return;
        var root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "../.."));
        var script = Path.Combine(root, "scripts", "Start-LocalRunner.ps1");
        var state = Path.Combine(root, "apps", "runner", "work", "local");
        if (!File.Exists(script))
        {
            logger.LogError("Local runner startup script is missing; automatic startup is unavailable.");
            return;
        }
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        var children = new Dictionary<Guid, Process>();
        try
        {
            await started.Task.WaitAsync(stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                foreach (var entry in children.ToArray())
                {
                    if (!entry.Value.HasExited) continue;
                    logger.LogWarning("Local runner {ProjectId} stopped (exit {ExitCode}); retrying startup.", entry.Key, entry.Value.ExitCode);
                    entry.Value.Dispose();
                    children.Remove(entry.Key);
                }
                if (Directory.Exists(state))
                    foreach (var folder in Directory.EnumerateDirectories(state))
                    {
                        if (!Guid.TryParse(Path.GetFileName(folder), out var projectId) || children.ContainsKey(projectId)
                            || !File.Exists(Path.Combine(folder, "credential.json"))
                            || !File.Exists(Path.Combine(folder, "controller.json")) || IsRunning(folder)) continue;
                        var info = new ProcessStartInfo
                        {
                            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
                            WorkingDirectory = root,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };
                        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-ProjectId", projectId.ToString() })
                            info.ArgumentList.Add(argument);
                        // The trusted script decrypts its own DPAPI credential; API secrets are not inherited.
                        info.Environment.Clear();
                        foreach (var key in new[] { "PATH", "PATHEXT", "COMSPEC", "SystemRoot", "WINDIR", "TEMP", "TMP", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "ProgramFiles", "ProgramFiles(x86)", "ProgramData" })
                            if (Environment.GetEnvironmentVariable(key) is { } value) info.Environment[key] = value;
                        try
                        {
                            var process = Process.Start(info) ?? throw new InvalidOperationException("Runner process did not start.");
                            children.Add(projectId, process);
                            _ = DrainAsync(process.StandardOutput);
                            _ = DrainAsync(process.StandardError);
                            logger.LogInformation("Starting configured local runner {ProjectId}.", projectId);
                        }
                        catch (System.ComponentModel.Win32Exception exception)
                        {
                            logger.LogError("Cannot start local runner {ProjectId} (system error {ErrorCode}).", projectId, exception.NativeErrorCode);
                        }
                    }
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            foreach (var process in children.Values)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                process.Dispose();
            }
        }
    }

    private static bool IsRunning(string folder)
    {
        var path = Path.Combine(folder, "worker.lock");
        if (!File.Exists(path)) return false;
        try { using var handle = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return false; }
        catch (IOException) { return true; }
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        // Bound memory and avoid exposing tool output or credentials through the API logs.
        try { var buffer = new char[4096]; while (await reader.ReadAsync(buffer) > 0) { } }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }
}
