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

    [Fact]
    public async Task CreatePatternsAllowNewFilesOnlyInsideTheirScopeAsync()
    {
        using var workspace = new ExecutionWorkspace(Path.GetFullPath("work/workspaces"));
        await File.WriteAllTextAsync(workspace.Resolve("Program.cs"), "existing");
        var tools = new WorkspaceTools(workspace, new DockerSandbox(), [], [], new("true", [], TimeSpan.FromSeconds(1)), ["**/*.cs"]);
        var created = await tools.ExecuteAsync(1, new("write", "Routes/PastelRoutes.cs", "class A {}", "missing"), default);
        Assert.True(created.Succeeded);
        // The agent may read and rewrite what it added without the operator listing each file.
        Assert.True((await tools.ExecuteAsync(2, new("read", "Routes/PastelRoutes.cs"), default)).Succeeded);
        Assert.True((await tools.ExecuteAsync(3, new("write", "Routes/PastelRoutes.cs", "class B {}", created.Hash), default)).Succeeded);
        // Existing files, other extensions, hidden and build folders, and a missing "missing" hash stay denied.
        Assert.False((await tools.ExecuteAsync(4, new("write", "Program.cs", "hacked", "missing"), default)).Succeeded);
        Assert.False((await tools.ExecuteAsync(4, new("write", "notes.txt", "x", "missing"), default)).Succeeded);
        Assert.False((await tools.ExecuteAsync(4, new("write", "bin/Evil.cs", "x", "missing"), default)).Succeeded);
        Assert.False((await tools.ExecuteAsync(4, new("write", ".git/Evil.cs", "x", "missing"), default)).Succeeded);
        Assert.False((await tools.ExecuteAsync(4, new("write", "../Evil.cs", "x", "missing"), default)).Succeeded);
        Assert.False((await tools.ExecuteAsync(4, new("write", "Other.cs", "x", null), default)).Succeeded);
        Assert.False((await tools.ExecuteAsync(4, new("read", "Program.cs"), default)).Succeeded);
        Assert.Throws<ArgumentException>(() => new WorkspaceTools(workspace, new DockerSandbox(), [], [], new("true", [], TimeSpan.FromSeconds(1)), ["../*.cs"]));
        for (var i = 0; i < 19; i++) Assert.True((await tools.ExecuteAsync(5 + i, new("write", $"Many/F{i}.cs", "x", "missing"), default)).Succeeded);
        Assert.False((await tools.ExecuteAsync(30, new("write", "Many/Extra.cs", "x", "missing"), default)).Succeeded);
    }
}
