using System.Text.Json;
using TeamFlow.Runner;
using Xunit;

namespace TeamFlow.Runner.Tests;

public sealed class HarnessTests
{
    [Fact]
    public async Task FixtureIsInvestigatedEditedAndTestedInDockerAsync()
    {
        using var workspace = new ExecutionWorkspace(Path.GetFullPath("work/workspaces"));
        using var controller = new ExecutionWorkspace(Path.GetFullPath("work/checkpoints"));
        await File.WriteAllTextAsync(workspace.Resolve("answer.txt"), "41");
        var context = Context();
        var checkpoints = new FileAgentCheckpoint(controller, context.ExecutionId);
        var tools = new WorkspaceTools(workspace, new DockerSandbox(), ["answer.txt"], ["answer.txt"],
            new SandboxCommand("/bin/sh", ["-c", "test \"$(cat answer.txt)\" = 42"], TimeSpan.FromSeconds(10)));
        var result = await new AgentHarness(new FixtureModel(), checkpoints, tools).RunAsync(context, 4, TimeSpan.FromSeconds(30), default);
        Assert.True(result.Validated);
        Assert.Equal("42", await File.ReadAllTextAsync(workspace.Resolve("answer.txt")));
        var persisted = JsonSerializer.Deserialize<PortableContext>(await File.ReadAllTextAsync(controller.Resolve($"{context.ExecutionId:N}.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(persisted);
        Assert.Equal(new[] { "read", "write", "test" }, persisted.Evidence.Select(e => e.Tool));
        Assert.All(persisted.Evidence, e => Assert.True(e.Succeeded));
    }

    [Theory]
    [InlineData("test", "step_limit")]
    [InlineData("write", "tool_failed")]
    [InlineData("finish", "validation_missing")]
    [InlineData("read", "step_limit")]
    public async Task FailuresAndLimitsCannotApproveAnExecutionAsync(string tool, string reason)
    {
        using var workspace = new ExecutionWorkspace(Path.GetFullPath("work/workspaces"));
        using var controller = new ExecutionWorkspace(Path.GetFullPath("work/checkpoints"));
        await File.WriteAllTextAsync(workspace.Resolve("answer.txt"), "41");
        var tools = new WorkspaceTools(workspace, new FailingSandbox(), ["answer.txt"], ["answer.txt"],
            new SandboxCommand("test", [], TimeSpan.FromSeconds(1)));
        var result = await new AgentHarness(new FixedModel(new AgentAction(tool, "answer.txt", "42", "stale")),
            new FixtureCheckpoint(controller.Resolve("checkpoint.json")), tools).RunAsync(Context(), 1, TimeSpan.FromSeconds(5), default);
        Assert.False(result.Validated);
        Assert.Equal(reason, result.Reason);
        Assert.Equal("41", await File.ReadAllTextAsync(workspace.Resolve("answer.txt")));
    }

    [Fact]
    public async Task ProtectedPathsAndCancellationAreEnforcedAsync()
    {
        using var workspace = new ExecutionWorkspace(Path.GetFullPath("work/workspaces"));
        using var controller = new ExecutionWorkspace(Path.GetFullPath("work/checkpoints"));
        var command = new SandboxCommand("test", [], TimeSpan.FromSeconds(1));
        Assert.Throws<ArgumentException>(() => new WorkspaceTools(workspace, new FailingSandbox(), [".git/config"], [], command));
        var tools = new WorkspaceTools(workspace, new FailingSandbox(), [], [], command);
        Assert.False((await tools.ExecuteAsync(1, new AgentAction("write", "../escape", "bad"), default)).Succeeded);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AgentHarness(new FixtureModel(),
            new FixtureCheckpoint(controller.Resolve("checkpoint.json")), tools).RunAsync(Context(), 4, TimeSpan.FromSeconds(5), cancelled.Token));
    }

    private static PortableContext Context() => new(1, Guid.NewGuid(), "Correct answer", ["Answer equals 42"], new string('a', 40), [], []);
    private sealed class FixtureModel : IAgentModel
    {
        public Task<AgentAction> NextAsync(PortableContext context, CancellationToken ct) => Task.FromResult(context.Evidence.Length switch
        {
            0 => new AgentAction("read", "answer.txt"),
            1 => new AgentAction("write", "answer.txt", "42", context.Evidence[0].Hash),
            2 => new AgentAction("test"),
            _ => new AgentAction("finish")
        });
    }
    private sealed class FixedModel(AgentAction action) : IAgentModel
    {
        public Task<AgentAction> NextAsync(PortableContext context, CancellationToken ct) => Task.FromResult(action);
    }
    private sealed class FixtureCheckpoint(string path) : IAgentCheckpoint
    {
        public string Path => path;
        public Task SaveAsync(PortableContext context, CancellationToken ct) => File.WriteAllTextAsync(path, JsonSerializer.Serialize(context), ct);
    }
    private sealed class FailingSandbox : ISandbox
    {
        public Task<SandboxResult> ExecuteAsync(ExecutionWorkspace workspace, SandboxCommand command, CancellationToken ct)
            => Task.FromResult(new SandboxResult(1, "", "", false, false));
    }
}
