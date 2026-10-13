using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace TeamFlow.Runner;

public sealed record SandboxCommand(string Executable, string[] Arguments, TimeSpan Timeout, bool ReadOnlyWorkspace = false);
public sealed record SandboxLimits(string Image = DockerSandbox.Image, int MemoryMb = 256, int Cpus = 1, int Pids = 128);
public sealed record SandboxResult(int ExitCode, string StandardOutput, string StandardError, bool OutputTruncated, bool TimedOut);

public interface ISandbox
{
    Task<SandboxResult> ExecuteAsync(ExecutionWorkspace workspace, SandboxCommand command, CancellationToken ct);
    SandboxLimits? Manifest => null;
}

public sealed partial class DockerSandbox(SandboxLimits? limits = null) : ISandbox
{
    public const string Image = "alpine@sha256:5291449c3df73caf6ed85e649dec1b9e818b39a5d8c871e97afc13e9cd5e8fa8";
    private const int OutputLimit = 16384;
    private readonly SandboxLimits _limits = limits ?? new();
    public SandboxLimits Manifest => _limits;

    public async Task<SandboxResult> ExecuteAsync(ExecutionWorkspace workspace, SandboxCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Executable) || command.Timeout < TimeSpan.FromMilliseconds(100)
            || command.Timeout > TimeSpan.FromHours(1) || command.Arguments.Length > 200)
            throw new ArgumentException("Invalid sandbox command or timeout.");
        if (_limits.MemoryMb is < 256 or > 2048 || _limits.Cpus is < 1 or > 4 || _limits.Pids is < 128 or > 512
            || !Regex.IsMatch(_limits.Image, @"\A[a-zA-Z0-9./:_-]+@sha256:[a-f0-9]{64}\z", RegexOptions.CultureInvariant))
            throw new ArgumentException("Sandbox image must be pinned by digest and resources bounded.");
        // Validate the mount again immediately before handing it to Docker.
        workspace.Resolve(".teamflow-probe");
        if (workspace.Path.Contains(',')) throw new ArgumentException("Docker mount paths cannot contain commas.");
        ct.ThrowIfCancellationRequested();
        var name = "teamflow-" + Guid.NewGuid().ToString("N");
        string[] arguments = ["run", "--name", name, "--label", "teamflow.owner=" + name, "--label", "teamflow.root=" + RootKey(Path.GetDirectoryName(workspace.Path) ?? throw new InvalidDataException("Missing workspace root.")),
            "--label", "teamflow.expires=" + DateTimeOffset.UtcNow.Add(command.Timeout).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), "--rm", "--pull", "never",
            "--network", "none", "--read-only", "--cap-drop", "ALL", "--security-opt", "no-new-privileges",
            "--user", "65532:65532", "--cpus", _limits.Cpus.ToString(System.Globalization.CultureInfo.InvariantCulture), "--memory", $"{_limits.MemoryMb}m", "--pids-limit", _limits.Pids.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--tmpfs", "/tmp:rw,nosuid,nodev,size=64m", "--mount", $"type=bind,source={workspace.Path},target=/workspace{(command.ReadOnlyWorkspace ? ",readonly" : "")}",
            "--workdir", "/workspace", _limits.Image, command.Executable, .. command.Arguments];
        using var process = Start(arguments);
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
        finally
        {
            // The random name is controller-owned; verify its label before removing it.
            await RemoveOwnedContainerAsync(name);
        }
        var output = await stdout;
        var error = await stderr;
        ct.ThrowIfCancellationRequested();
        return new(process.ExitCode, output.Text, error.Text, output.Truncated || error.Truncated, timedOut);
    }

    private static Process Start(IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo("docker") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        // No host secrets are inherited by the CLI or passed into the container.
        var permitted = new[] { "PATH", "SystemRoot", "USERPROFILE", "HOME", "LOCALAPPDATA", "APPDATA", "TEMP", "TMP" };
        var environment = permitted.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        start.Environment.Clear();
        foreach (var (key, value) in environment) if (value is not null) start.Environment[key] = value;
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new InvalidOperationException("Docker could not be started.");
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

    private static async Task RemoveOwnedContainerAsync(string name)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var inspect = Start(["inspect", "--format", "{{index .Config.Labels \"teamflow.owner\"}}", name]);
        var output = CaptureAsync(inspect.StandardOutput);
        var error = CaptureAsync(inspect.StandardError);
        try
        {
            await inspect.WaitForExitAsync(cleanup.Token);
            var label = await output;
            var inspectionError = await error;
            if (inspect.ExitCode != 0)
            {
                if (inspectionError.Text.Contains("No such object: " + name, StringComparison.OrdinalIgnoreCase)
                    || inspectionError.Text.Contains("No such container: " + name, StringComparison.OrdinalIgnoreCase)) return;
                throw new InvalidOperationException("Cannot verify sandbox cleanup; check Docker availability.");
            }
            if (label.Text.Trim() != name) throw new InvalidOperationException("Container ownership verification failed.");
            using var remove = Start(["rm", "--force", name]);
            var removedOutput = CaptureAsync(remove.StandardOutput);
            var removedError = CaptureAsync(remove.StandardError);
            try { await remove.WaitForExitAsync(cleanup.Token); }
            catch (OperationCanceledException) { if (!remove.HasExited) remove.Kill(true); throw; }
            await removedOutput;
            await removedError;
            if (remove.ExitCode != 0) throw new InvalidOperationException("Sandbox cleanup failed; check the owned container.");
        }
        catch (OperationCanceledException)
        {
            if (!inspect.HasExited) inspect.Kill(true);
            throw new InvalidOperationException("Sandbox cleanup timed out; check the owned container.");
        }
    }
}
