using TeamFlow.Runner;
using Xunit;

namespace TeamFlow.Runner.Tests;

public sealed class FileCreationTests
{
    [Fact]
    public async Task CreationNeedsAnApprovedPathAndCannotOverwriteExistingFilesAsync()
    {
        using var workspace = new ExecutionWorkspace(Path.GetFullPath("work/workspaces"));
        var tools = new WorkspaceTools(workspace, new DockerSandbox(), ["src/new.txt"], ["src/new.txt"],
            new("true", [], TimeSpan.FromSeconds(1)));
        Assert.False((await tools.ExecuteAsync(1, new("write", "other.txt", "42", "missing"), default)).Succeeded);
        Assert.False((await tools.ExecuteAsync(1, new("write", "src/new.txt", "42", "stale"), default)).Succeeded);
        var absent = await tools.ExecuteAsync(1, new("read", "src/new.txt"), default);
        Assert.True(absent.Succeeded);
        Assert.Contains("expectedHash=missing", absent.Detail);
        Assert.Null(absent.Hash);
        var created = await tools.ExecuteAsync(1, new("write", "src/new.txt", "42", "missing"), default);
        Assert.True(created.Succeeded);
        Assert.Equal("42", await File.ReadAllTextAsync(workspace.Resolve("src/new.txt")));
        Assert.False((await tools.ExecuteAsync(2, new("write", "src/new.txt", "43", "missing"), default)).Succeeded);
        Assert.True((await tools.ExecuteAsync(2, new("write", "src/new.txt", "43", created.Hash), default)).Succeeded);
    }
}
