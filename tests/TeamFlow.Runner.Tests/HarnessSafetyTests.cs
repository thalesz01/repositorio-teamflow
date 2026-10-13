using TeamFlow.Runner;
using Xunit;

namespace TeamFlow.Runner.Tests;

public sealed class HarnessSafetyTests
{
    [Fact]
    public async Task TestCodeCannotChangeTheCheckoutItIsValidatingAsync()
    {
        using var workspace = new ExecutionWorkspace(Path.GetFullPath("work/workspaces"));
        await File.WriteAllTextAsync(workspace.Resolve("answer.txt"), "41");
        var tools = new WorkspaceTools(workspace, new DockerSandbox(), ["answer.txt"], ["answer.txt"],
            new("/bin/sh", ["-c", "echo 42 > answer.txt"], TimeSpan.FromSeconds(10)));
        var evidence = await tools.ExecuteAsync(1, new AgentAction("test"), default);
        Assert.False(evidence.Succeeded);
        Assert.Equal("41", await File.ReadAllTextAsync(workspace.Resolve("answer.txt")));
    }

    [Fact]
    public async Task ControllerImageAndResourceConfigurationIsBoundedAsync()
    {
        using var workspace = new ExecutionWorkspace(Path.GetFullPath("work/workspaces"));
        var command = new SandboxCommand("true", [], TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<ArgumentException>(() => new DockerSandbox(new("alpine:latest")).ExecuteAsync(workspace, command, default));
        await Assert.ThrowsAsync<ArgumentException>(() => new DockerSandbox(new(MemoryMb: 2049)).ExecuteAsync(workspace, command, default));
    }
}
