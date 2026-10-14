using TeamFlow.Runner;
using Xunit;

namespace TeamFlow.Runner.Tests;

// Analysis tasks (ADR 0014): the agent may read and search the readable files, never write or test, and ends with an answer.
public sealed class AnalysisModeTests
{
    private static async Task<(ExecutionWorkspace Workspace, WorkspaceTools Tools)> ProjectAsync()
    {
        var workspace = new ExecutionWorkspace(Path.GetFullPath("work/analysis"));
        await File.WriteAllTextAsync(workspace.Resolve("Orders.cs"), "class Orders\n{\n    bool Accept(decimal discount) => discount <= 0.2m; // max discount\n}\n");
        await File.WriteAllTextAsync(workspace.Resolve("Customers.cs"), "class Customers { }\n");
        await File.WriteAllTextAsync(workspace.Resolve("Secrets.cs"), "const string Discount = \"not readable\";\n");
        var tools = new WorkspaceTools(workspace, new DockerSandbox(), ["Orders.cs", "Customers.cs"], [], new("true", [], TimeSpan.FromSeconds(1)), null, testsAllowed: false);
        return (workspace, tools);
    }

    [Fact]
    public async Task SearchFindsTextOnlyInTheReadableFilesAsync()
    {
        var (workspace, tools) = await ProjectAsync();
        using var _ = workspace;
        var found = await tools.ExecuteAsync(1, new("search", null, "DISCOUNT"), default);
        Assert.True(found.Succeeded);
        Assert.Contains("Orders.cs:3:", found.Detail);
        Assert.DoesNotContain("Secrets.cs", found.Detail);
        var none = await tools.ExecuteAsync(2, new("search", null, "nothing-like-this"), default);
        Assert.True(none.Succeeded); Assert.Contains("no matches in 2 readable files", none.Detail);
        Assert.False((await tools.ExecuteAsync(3, new("search", null, "a"), default)).Succeeded);
        Assert.False((await tools.ExecuteAsync(4, new("test"), default)).Succeeded);
        Assert.False((await tools.ExecuteAsync(5, new("write", "Orders.cs", "x", "missing"), default)).Succeeded);
    }

    [Fact]
    public async Task AnalysisIsAnsweredAfterConsultingFilesAndToleratesMistakesButNeverWritesOrTestsAsync()
    {
        var (workspace, tools) = await ProjectAsync();
        using var _ = workspace;
        using var controller = new ExecutionWorkspace(Path.GetFullPath("work/analysis-checkpoints"));
        PortableContext Start() => new(1, Guid.NewGuid(), "What is the discount rule?", ["Answered"], new string('a', 40), [], []);
        async Task<HarnessResult> RunAsync(params AgentAction[] script)
        {
            var context = Start();
            return await new AgentHarness(new Script(script), new FileAgentCheckpoint(controller, context.ExecutionId), tools, HarnessMode.Analysis)
                .RunAsync(context, 20, TimeSpan.FromSeconds(5), default);
        }
        var answered = await RunAsync(new("search", null, "discount"), new("read", "Secrets.cs"), new("write", "Orders.cs", "x", "missing"), new("test"),
            new("read", "Orders.cs"), new("finish", null, "Discounts above 20% are rejected in Orders.Accept."));
        Assert.True(answered.Validated);
        Assert.Equal("analysis_answered", answered.Reason);
        Assert.DoesNotContain(answered.Context.Evidence, e => e.Tool is "write" or "test");
        Assert.Contains(answered.Context.Evidence, e => e.Tool == "rejected");
        Assert.Equal("validation_missing", (await RunAsync(new AgentAction("finish", null, "I did not look at anything."))).Reason);
        Assert.Equal("validation_missing", (await RunAsync(new("read", "Orders.cs"), new("finish", null, "  "))).Reason);
    }

    private sealed class Script(AgentAction[] actions) : IAgentModel
    {
        private int _next;
        public Task<AgentAction> NextAsync(PortableContext context, CancellationToken ct) => Task.FromResult(actions[_next++]);
    }
}
