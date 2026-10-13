using System.Diagnostics;
using TeamFlow.Runner;
using Xunit;

namespace TeamFlow.Runner.Tests;

public sealed class SandboxTests
{
    private static string Root => Path.GetFullPath(Path.Combine("work", "workspaces"));

    [Fact]
    public void WorkspacesRejectTraversalAndDoNotDeleteTheirNeighbors()
    {
        using var first = new ExecutionWorkspace(Root);
        using var second = new ExecutionWorkspace(Root);
        File.WriteAllText(second.Resolve("keep.txt"), "keep");
        Assert.Throws<ArgumentException>(() => first.Resolve("../escape"));
        Assert.Throws<ArgumentException>(() => first.Resolve("..\\escape"));
        Assert.Throws<ArgumentException>(() => first.Resolve(second.Path));
        Assert.Throws<ArgumentException>(() => first.Resolve("file:stream"));
        first.Dispose();
        Assert.Equal("keep", File.ReadAllText(second.Resolve("keep.txt")));
        Assert.False(Directory.Exists(first.Path));
    }

    [Fact]
    public async Task LinksAreRejectedAndCleanupDoesNotFollowThemAsync()
    {
        using var workspace = new ExecutionWorkspace(Root);
        using var outside = new ExecutionWorkspace(Root);
        File.WriteAllText(outside.Resolve("keep.txt"), "keep");
        var link = workspace.Resolve("link");
        if (OperatingSystem.IsWindows())
        {
            // A junction needs no symlink privilege; both paths are test-owned absolute directories.
            var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.Arguments = $"/c mklink /J \"{link}\" \"{outside.Path}\"";
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot create test junction.");
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
        }
        else Directory.CreateSymbolicLink(link, outside.Path);
        Assert.Throws<ArgumentException>(() => workspace.Resolve("link/keep.txt"));
        Assert.Throws<ArgumentException>(() => new ExecutionWorkspace(link));
        workspace.Dispose();
        Assert.Equal("keep", File.ReadAllText(outside.Resolve("keep.txt")));
    }

    [Fact]
    public async Task ContainerHasRestrictedIdentityNetworkResourcesAndMountsAsync()
    {
        using var workspace = new ExecutionWorkspace(Root);
        using var neighbor = new ExecutionWorkspace(Root);
        File.WriteAllText(neighbor.Resolve("secret.txt"), "neighbor-secret");
        var command = new SandboxCommand("/bin/sh", ["-c", "test $(id -u) = 65532 && test ! -e /var/run/docker.sock && test ! -e /workspace/../secret.txt && test $(ls /sys/class/net | wc -l) = 1 && test $(cat /sys/fs/cgroup/memory.max) = 268435456 && test $(cat /sys/fs/cgroup/pids.max) = 128 && ! touch /forbidden && echo ok > result.txt && cat result.txt"], TimeSpan.FromSeconds(20));
        var result = await new DockerSandbox().ExecuteAsync(workspace, command, CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Contains("ok", result.StandardOutput);
        Assert.Equal("ok", File.ReadAllText(workspace.Resolve("result.txt")).Trim());
        Assert.Equal("neighbor-secret", File.ReadAllText(neighbor.Resolve("secret.txt")));
    }

    [Fact]
    public async Task TimeoutStopsAndRemovesTheContainerAsync()
    {
        using var workspace = new ExecutionWorkspace(Root);
        var result = await new DockerSandbox().ExecuteAsync(workspace,
            new("/bin/sh", ["-c", "echo started > started.txt; sleep 30; echo late > late.txt"], TimeSpan.FromSeconds(2)), CancellationToken.None);
        Assert.True(result.TimedOut);
        Assert.False(File.Exists(workspace.Resolve("late.txt")));
    }

    [Fact]
    public async Task CancellationStopsWorkAsync()
    {
        using var workspace = new ExecutionWorkspace(Root);
        using var cancellation = new CancellationTokenSource();
        var running = new DockerSandbox().ExecuteAsync(workspace,
            new("/bin/sh", ["-c", "echo started > started.txt; sleep 30; echo late > late.txt"], TimeSpan.FromSeconds(40)), cancellation.Token);
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!File.Exists(workspace.Resolve("started.txt")) && DateTime.UtcNow < deadline) await Task.Delay(50);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.True(File.Exists(workspace.Resolve("started.txt")));
        Assert.False(File.Exists(workspace.Resolve("late.txt")));
    }

    [Fact]
    public async Task ArgumentsRemainLiteralAndOutputIsBoundedAsync()
    {
        using var workspace = new ExecutionWorkspace(Root);
        var literal = await new DockerSandbox().ExecuteAsync(workspace,
            new("/bin/echo", ["--privileged", "$(touch /workspace/injected.txt)"], TimeSpan.FromSeconds(20)), CancellationToken.None);
        Assert.Equal(0, literal.ExitCode);
        Assert.Contains("$(touch", literal.StandardOutput);
        Assert.False(File.Exists(workspace.Resolve("injected.txt")));
        var bounded = await new DockerSandbox().ExecuteAsync(workspace,
            new("/bin/sh", ["-c", "yes output | head -c 50000"], TimeSpan.FromSeconds(20)), CancellationToken.None);
        Assert.True(bounded.OutputTruncated);
        Assert.Equal(16384, bounded.StandardOutput.Length);
    }
}
