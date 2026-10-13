using System.Diagnostics;
using TeamFlow.Runner;
using Xunit;

namespace TeamFlow.Runner.Tests;

public sealed class GitCheckoutTests
{
    private static string Root => Path.GetFullPath(Path.Combine("work", "workspaces"));

    [Fact]
    public async Task FixtureCheckoutPinsCommitCreatesBranchAndCleansOnlyItsWorkspaceAsync()
    {
        using var fixture = new ExecutionWorkspace(Root);
        using var checkout = new ExecutionWorkspace(Root);
        await GitAsync(fixture.Path, ["init", "-b", "main"]);
        File.WriteAllText(fixture.Resolve("source.txt"), "fixture content");
        await GitAsync(fixture.Path, ["add", "source.txt"]);
        await GitAsync(fixture.Path, ["-c", "user.name=Fixture", "-c", "user.email=fixture@example.test", "commit", "-m", "Fixture base"]);
        var commit = (await GitAsync(fixture.Path, ["rev-parse", "HEAD"])).Trim();
        var executionId = Guid.NewGuid();
        var prepared = await new GitCheckout().PrepareFixtureAsync(checkout, fixture.Path, commit, executionId, CancellationToken.None);
        Assert.Equal(commit, prepared.BaseCommit);
        Assert.Equal($"teamflow/{executionId:N}", prepared.Branch);
        Assert.Equal("fixture content", File.ReadAllText(checkout.Resolve("source.txt")));
        var config = File.ReadAllText(checkout.Resolve(".git/config"));
        Assert.DoesNotContain("AUTHORIZATION", config);
        Assert.DoesNotContain("extraheader", config);
        checkout.Dispose();
        Assert.False(Directory.Exists(checkout.Path));
        Assert.Equal("fixture content", File.ReadAllText(fixture.Resolve("source.txt")));
    }

    [Fact]
    public async Task CheckoutRejectsUnsafeUrlsAndInvalidCommitsBeforeNetworkAsync()
    {
        using var workspace = new ExecutionWorkspace(Root);
        var checkout = new GitCheckout();
        var source = new CheckoutSource("https://github.com/owner/repo.git", new string('a', 40), $"teamflow/{Guid.NewGuid():N}");
        foreach (var url in new[] { "file:///etc/passwd", "https://github.com.evil.test/owner/repo.git", "https://token@github.com/owner/repo.git", "ssh://github.com/owner/repo.git" })
            await Assert.ThrowsAsync<ArgumentException>(() => checkout.PrepareAsync(workspace, source with { RemoteUrl = url }, "fixture-token", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => checkout.PrepareAsync(workspace, source with { BaseCommit = "--upload-pack=evil" }, "fixture-token", CancellationToken.None));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => checkout.PrepareAsync(workspace, source, "fixture-token", cancelled.Token));
        Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.Path));
    }

    [Fact]
    public async Task MissingCommitIsFailureAndLeavesOtherDirectoriesIntactAsync()
    {
        using var fixture = new ExecutionWorkspace(Root);
        using var workspace = new ExecutionWorkspace(Root);
        await GitAsync(fixture.Path, ["init", "-b", "main"]);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new GitCheckout().PrepareFixtureAsync(workspace,
            fixture.Path, new string('a', 40), Guid.NewGuid(), CancellationToken.None));
        Assert.Contains("checkout failed", error.Message);
        workspace.Dispose();
        Assert.True(Directory.Exists(fixture.Resolve(".git")));
    }

    [Fact]
    public async Task AuthorizedLocalCheckoutPreservesUncommittedSourceFilesAsync()
    {
        using var fixture = new ExecutionWorkspace(Root);
        using var workspace = new ExecutionWorkspace(Root);
        await GitAsync(fixture.Path, ["init", "-b", "main"]);
        File.WriteAllText(fixture.Resolve("source.txt"), "committed");
        await GitAsync(fixture.Path, ["add", "source.txt"]);
        await GitAsync(fixture.Path, ["-c", "user.name=Fixture", "-c", "user.email=fixture@example.test", "commit", "-m", "Base"]);
        var commit = (await GitAsync(fixture.Path, ["rev-parse", "HEAD"])).Trim();
        File.WriteAllText(fixture.Resolve("source.txt"), "uncommitted");
        var source = new CheckoutSource(fixture.Path, commit, $"teamflow/{Guid.NewGuid():N}", fixture.Path);
        await Assert.ThrowsAsync<ArgumentException>(() => new GitCheckout().PrepareAsync(workspace, source, "wrong-grant", CancellationToken.None));
        var prepared = await new GitCheckout().PrepareAsync(workspace, source, "local", CancellationToken.None);
        Assert.Equal(commit, prepared.BaseCommit);
        Assert.Equal("committed", File.ReadAllText(workspace.Resolve("source.txt")));
        Assert.Equal("uncommitted", File.ReadAllText(fixture.Resolve("source.txt")));
    }

    private static async Task<string> GitAsync(string directory, string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Git unavailable.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
        await error;
        return await output;
    }
}
