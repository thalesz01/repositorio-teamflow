using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using TeamFlow.Runner;
using Xunit;

namespace TeamFlow.Runner.Tests;

public sealed class DockerRecoveryTests
{
    [Fact]
    public async Task RecoveryRemovesOnlyExpiredContainersOfTheConfiguredRootAsync()
    {
        var root = Path.GetFullPath(Path.Combine("work", "recovery-" + Guid.NewGuid().ToString("N")));
        var rootKey = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(root)));
        var expired = "teamflow-" + Guid.NewGuid().ToString("N");
        var active = "teamflow-" + Guid.NewGuid().ToString("N");
        try
        {
            await StartAsync(expired, DateTimeOffset.UtcNow.AddMinutes(-1));
            await StartAsync(active, DateTimeOffset.UtcNow.AddMinutes(5));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await new DockerSandbox().CleanupExpiredAsync(root, timeout.Token);
            Assert.NotEqual(0, await DockerAsync("inspect", expired));
            Assert.Equal(0, await DockerAsync("inspect", active));
        }
        finally
        {
            await DockerAsync("rm", "--force", expired);
            await DockerAsync("rm", "--force", active);
        }

        async Task StartAsync(string name, DateTimeOffset expiry)
        {
            Assert.Equal(0, await DockerAsync("run", "-d", "--rm", "--pull", "never", "--name", name,
                "--label", "teamflow.owner=" + name, "--label", "teamflow.root=" + rootKey,
                "--label", "teamflow.expires=" + expiry.ToUnixTimeSeconds(), "--network", "none", "--read-only",
                "--cap-drop", "ALL", "--security-opt", "no-new-privileges", "--user", "65532:65532",
                "--memory", "256m", "--cpus", "1", "--pids-limit", "128", DockerSandbox.Image, "sleep", "60"));
        }
    }

    private static async Task<int> DockerAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo("docker") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Docker fixture could not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); throw; }
        await output;
        await error;
        return process.ExitCode;
    }
}
