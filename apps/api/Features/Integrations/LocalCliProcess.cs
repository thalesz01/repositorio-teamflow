using System.Diagnostics;
using System.Text;

namespace TeamFlow.Api.Features.Integrations;

public static class LocalCliProcess
{
    public static bool Installed(string executable) => Path.IsPathFullyQualified(executable) && File.Exists(executable)
        && Path.GetExtension(executable).Equals(".exe", StringComparison.OrdinalIgnoreCase);

    public static async Task<(int ExitCode, string Output)> RunAsync(string executable, string[] arguments, string? input, int seconds, CancellationToken ct)
    {
        if (!Installed(executable)) throw new InvalidOperationException("CLI executable unavailable.");
        var directory = Path.Combine(Path.GetTempPath(), "teamflow-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var start = new ProcessStartInfo(executable)
            {
                WorkingDirectory = directory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false)
            };
            start.Environment.Clear();
            foreach (var key in new[] { "PATH", "SystemRoot", "TEMP", "TMP", "USERPROFILE", "HOME", "APPDATA", "LOCALAPPDATA", "CLAUDE_CONFIG_DIR" })
                if (Environment.GetEnvironmentVariable(key) is { } value) start.Environment[key] = value;
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start CLI.");
            var output = CaptureAsync(process.StandardOutput); var error = CaptureAsync(process.StandardError);
            try
            {
                if (input is not null) await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token);
                process.StandardInput.Close(); await process.WaitForExitAsync(timeout.Token);
            }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); await Task.WhenAll(output, error); }
            return (process.ExitCode, await output);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static async Task<string> CaptureAsync(StreamReader reader)
    {
        var text = new StringBuilder(); var buffer = new char[4096]; var overflow = false; int count;
        while ((count = await reader.ReadAsync(buffer)) > 0) { var take = Math.Min(count, 131072 - text.Length); text.Append(buffer, 0, take); overflow |= take < count; }
        if (overflow) throw new IOException("CLI output exceeds limit.");
        return text.ToString();
    }
}
