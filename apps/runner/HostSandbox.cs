using System.Diagnostics;
using System.Text;

namespace TeamFlow.Runner;

/// <summary>
/// Runs the controller's test command directly on this computer (ADR 0013). There is no container: the command and any build
/// logic the agent wrote can use the network and the user's files with the user's permissions. It exists for trusted personal
/// folders, is chosen per folder in controller.json and is never the default. The command runs in a temporary copy of the checkout, so
/// the execution workspace stays free of build output, and the copy is deleted afterwards.
/// </summary>
public sealed class HostSandbox : ISandbox
{
    private const int OutputLimit = 16384;
    private const int MaxFiles = 5000;
    private const long MaxBytes = 100L * 1024 * 1024;
    // The agent's code runs as this user, so only what the toolchain needs is inherited: never the runner token or API settings.
    private static readonly string[] Inherited = ["PATH", "PATHEXT", "SystemRoot", "WINDIR", "COMSPEC", "USERPROFILE", "HOME", "APPDATA", "LOCALAPPDATA",
        "ProgramFiles", "ProgramFiles(x86)", "ProgramData", "ProgramW6432", "NUMBER_OF_PROCESSORS", "PROCESSOR_ARCHITECTURE"];

    public SandboxLimits? Manifest => new("host", 0, 0, 0);

    public async Task<SandboxResult> ExecuteAsync(ExecutionWorkspace workspace, SandboxCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Executable) || command.Timeout < TimeSpan.FromMilliseconds(100)
            || command.Timeout > TimeSpan.FromHours(1) || command.Arguments.Length > 200)
            throw new ArgumentException("Invalid host command or timeout.");
        workspace.Resolve(".teamflow-probe");
        var copy = Path.Combine(Path.GetTempPath(), "teamflow-host-" + Guid.NewGuid().ToString("N"));
        try
        {
            Copy(workspace.Path, copy);
            var start = new ProcessStartInfo(command.Executable)
            {
                WorkingDirectory = copy,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.Environment.Clear();
            foreach (var key in Inherited)
                if (Environment.GetEnvironmentVariable(key) is { } value) start.Environment[key] = value;
            start.Environment["TEMP"] = start.Environment["TMP"] = copy + "-tmp";
            Directory.CreateDirectory(copy + "-tmp");
            // Keep the .NET toolchain quiet and free of background servers that would outlive the process tree.
            foreach (var (key, value) in new Dictionary<string, string>
            {
                ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
                ["DOTNET_NOLOGO"] = "1",
                ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
                ["MSBUILDDISABLENODEREUSE"] = "1",
                ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0",
                ["UseSharedCompilation"] = "false"
            })
                start.Environment[key] = value;
            foreach (var argument in command.Arguments) start.ArgumentList.Add(argument);
            Process process;
            try { process = Process.Start(start) ?? throw new InvalidOperationException("The command could not be started."); }
            catch (System.ComponentModel.Win32Exception)
            {
                return new(127, "", $"Executable '{command.Executable}' was not found on this computer.", false, false);
            }
            using (process)
            {
                process.StandardInput.Close();
                var stdout = CaptureAsync(process.StandardOutput);
                var stderr = CaptureAsync(process.StandardError);
                using var timeout = new CancellationTokenSource(command.Timeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
                var timedOut = false;
                try { await process.WaitForExitAsync(linked.Token); }
                catch (OperationCanceledException)
                {
                    timedOut = timeout.IsCancellationRequested && !ct.IsCancellationRequested;
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
                var output = await stdout;
                var error = await stderr;
                ct.ThrowIfCancellationRequested();
                return new(process.ExitCode, output.Text, error.Text, output.Truncated || error.Truncated, timedOut);
            }
        }
        finally
        {
            Delete(copy);
            Delete(copy + "-tmp");
        }
    }

    // Plain files and folders only; links, the Git directory and oversized trees are not copied.
    private static void Copy(string source, string target)
    {
        Directory.CreateDirectory(target);
        long bytes = 0; var files = 0;
        var pending = new Stack<(string From, string To)>([(source, target)]);
        while (pending.Count > 0)
        {
            var (from, to) = pending.Pop();
            foreach (var entry in new DirectoryInfo(from).EnumerateFileSystemInfos())
            {
                if (entry.LinkTarget is not null || (entry.Attributes & FileAttributes.ReparsePoint) != 0 || entry.Name == ".git") continue;
                if (entry is DirectoryInfo directory)
                {
                    Directory.CreateDirectory(Path.Combine(to, entry.Name));
                    pending.Push((directory.FullName, Path.Combine(to, entry.Name)));
                    continue;
                }
                var file = (FileInfo)entry;
                bytes += file.Length;
                if (++files > MaxFiles || bytes > MaxBytes) throw new ArgumentException("The workspace is too large to build on this computer.");
                file.CopyTo(Path.Combine(to, entry.Name));
            }
        }
    }

    private static void Delete(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(path, recursive: true);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { /* a leftover temp folder is harmless; the next run uses a new one */ }
    }

    private static async Task<(string Text, bool Truncated)> CaptureAsync(StreamReader reader)
    {
        var text = new StringBuilder();
        var buffer = new char[2048];
        var truncated = false;
        int count;
        while ((count = await reader.ReadAsync(buffer)) > 0)
        {
            var take = Math.Min(count, OutputLimit - text.Length);
            text.Append(buffer, 0, take);
            truncated |= take < count;
        }
        return (text.ToString(), truncated);
    }
}
