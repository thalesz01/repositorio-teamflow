using TeamFlow.Runner;
using Xunit;

namespace TeamFlow.Runner.Tests;

public sealed class PolicyPatternTests
{
    private static readonly SandboxCommand Test = new("test", [], TimeSpan.FromSeconds(1));

    [Fact]
    public void PatternsExpandToBoundedVisibleFilesAndKeepExactPaths()
    {
        using var workspace = new ExecutionWorkspace(Path.GetFullPath("work/patterns"));
        Write(workspace, "src/App.cs", "class App;");
        Write(workspace, "src/Models/Item.cs", "class Item;");
        Write(workspace, "src/notes.txt", "notes");
        Write(workspace, "src/bin/Debug/Generated.cs", "class Generated;");
        Write(workspace, ".config/Secret.cs", "class Secret;");
        Write(workspace, "src/Large.cs", new string('a', WorkspaceTools.FileLimit + 1));
        Write(workspace, "README.md", "readme");

        var expanded = ToolPolicyPatterns.Expand(new(["README.md", "src/**/*.cs"], ["src/*.cs", "src/New.cs"], Test), workspace);

        Assert.Equal(new[] { "README.md", "src/App.cs", "src/Models/Item.cs" }, expanded.Readable);
        Assert.Equal(new[] { "src/New.cs", "src/App.cs" }, expanded.Editable);
        Assert.Same(Test, expanded.Test);
        _ = new WorkspaceTools(workspace, new NoSandbox(), expanded.Readable, expanded.Editable, expanded.Test);
    }

    [Fact]
    public void ExactPolicyIsUnchangedAndUnsafeOrOversizedPatternsAreRejected()
    {
        using var workspace = new ExecutionWorkspace(Path.GetFullPath("work/patterns"));
        var exact = new RunnerToolPolicy(["answer.txt"], ["answer.txt"], Test);
        Assert.Same(exact, ToolPolicyPatterns.Expand(exact, workspace));
        Assert.Throws<ArgumentException>(() => ToolPolicyPatterns.Expand(new(["../**/*.cs"], [], Test), workspace));
        Assert.Throws<ArgumentException>(() => ToolPolicyPatterns.Expand(new([@"src\*.cs"], [], Test), workspace));
        for (var i = 0; i <= ToolPolicyPatterns.MaxPaths; i++) Write(workspace, $"many/file{i}.txt", "x");
        Assert.Throws<ArgumentException>(() => ToolPolicyPatterns.Expand(new(["many/*.txt"], [], Test), workspace));
    }

    private static void Write(ExecutionWorkspace workspace, string relative, string content)
    {
        var path = Path.Combine(workspace.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? workspace.Path);
        File.WriteAllText(path, content);
    }

    private sealed class NoSandbox : ISandbox
    {
        public Task<SandboxResult> ExecuteAsync(ExecutionWorkspace workspace, SandboxCommand command, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
