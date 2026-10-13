using TeamFlow.Runner;
using Xunit;

namespace TeamFlow.Runner.Tests;

public sealed class WorkspaceRecoveryTests
{
    [Fact]
    public void CleanupRequiresOwnedMarkerAndAgeAndPreservesUnrecognizedDirectories()
    {
        using var parent = new ExecutionWorkspace(Path.GetFullPath("work/parent-recovery"));
        var root = parent.Resolve("workspaces");
        using var stale = new ExecutionWorkspace(root);
        using var recent = new ExecutionWorkspace(root);
        var unknown = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(unknown);
        File.WriteAllText(Path.Combine(unknown, "keep.txt"), "keep");
        var marker = Path.Combine(root, ".owners", Path.GetFileName(stale.Path));
        File.SetCreationTimeUtc(marker, DateTime.UtcNow.AddHours(-2));
        ExecutionWorkspace.CleanupExpired(root, DateTimeOffset.UtcNow);
        Assert.False(Directory.Exists(stale.Path));
        Assert.True(Directory.Exists(recent.Path));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(unknown, "keep.txt")));
    }
}
