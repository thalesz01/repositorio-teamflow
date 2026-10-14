using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TeamFlow.Runner;
using Xunit;

namespace TeamFlow.Runner.Tests;

public sealed class TransferRunnerTests
{
    [Fact]
    public async Task TransferRestoresConfirmedChangesButRequiresFreshTestsAndControllerPathsAsync()
    {
        using var workspace = new ExecutionWorkspace(Path.GetFullPath("work/transfer"));
        await File.WriteAllTextAsync(workspace.Resolve("answer.txt"), "41");
        static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        var parentId = Guid.NewGuid();
        var parent = new PortableContext(1, parentId, "Original goal", ["Answer is 42"], new string('a', 40),
            [new(1, "write", "answer.txt", true, "file_updated", Hash("42")), new(2, "test", null, true, "prior-test-passed")], []);
        var transfer = new PortableTransfer(parentId, parent.BaseCommit, new string('b', 40), parent, [new(1, "answer.txt", "42", Hash("41"), Hash("42"))]);
        var element = JsonSerializer.SerializeToElement(transfer, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var tools = new WorkspaceTools(workspace, new DockerSandbox(), ["answer.txt"], ["answer.txt"], new("true", [], TimeSpan.FromSeconds(1)));
        await TransferRestore.ApplyAsync(element, parent.BaseCommit, workspace, tools, default);
        Assert.Equal("42", await File.ReadAllTextAsync(workspace.Resolve("answer.txt")));
        var writtenAt = File.GetLastWriteTimeUtc(workspace.Resolve("answer.txt"));
        await TransferRestore.ApplyAsync(element, parent.BaseCommit, workspace, tools, default);
        Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(workspace.Resolve("answer.txt")));
        var initial = new PortableContext(1, Guid.NewGuid(), "New goal", ["Add negative values"], parent.BaseCommit, [], [], History: element);
        using var controller = new ExecutionWorkspace(Path.GetFullPath("work/transfer-checkpoints"));
        var result = await new AgentHarness(new FinishOnly(), new FileAgentCheckpoint(controller, initial.ExecutionId), tools)
            .RunAsync(initial, 5, TimeSpan.FromSeconds(1), default);
        Assert.False(result.Validated); Assert.Equal("validation_missing", result.Reason); Assert.Empty(result.Context.Evidence);
        await File.WriteAllTextAsync(workspace.Resolve("answer.txt"), "conflict");
        await Assert.ThrowsAsync<InvalidDataException>(() => TransferRestore.ApplyAsync(element, parent.BaseCommit, workspace, tools, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => TransferRestore.ApplyAsync(element, new string('c', 40), workspace, tools, default));
        await File.WriteAllTextAsync(workspace.Resolve("answer.txt"), "41");
        var restricted = new WorkspaceTools(workspace, new DockerSandbox(), ["answer.txt"], [], new("true", [], TimeSpan.FromSeconds(1)));
        await Assert.ThrowsAsync<InvalidDataException>(() => TransferRestore.ApplyAsync(element, parent.BaseCommit, workspace, restricted, default));
    }
    [Fact]
    public async Task TransferAcceptsATestThatFailedAndWasFixedButNotAFailingFinalStateAsync()
    {
        using var workspace = new ExecutionWorkspace(Path.GetFullPath("work/transfer-corrected"));
        await File.WriteAllTextAsync(workspace.Resolve("answer.txt"), "41");
        static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        const string failed = "{\"ExitCode\":1,\"TimedOut\":false,\"OutputTruncated\":false}";
        var parentId = Guid.NewGuid();
        PortableTransfer Transfer(params ToolEvidence[] evidence)
        {
            var context = new PortableContext(1, parentId, "Original goal", ["Answer is 42"], new string('a', 40), evidence, []);
            return new(parentId, context.BaseCommit, new string('b', 40), context, [new(1, "answer.txt", "42", Hash("41"), Hash("42"))]);
        }
        static JsonElement Json(PortableTransfer value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var tools = new WorkspaceTools(workspace, new DockerSandbox(), ["answer.txt"], ["answer.txt"], new("true", [], TimeSpan.FromSeconds(1)));
        var corrected = Transfer(new(1, "write", "answer.txt", true, "file_updated", Hash("42")), new(2, "test", null, false, failed),
            new(3, "write", "answer.txt", true, "file_updated", Hash("42")), new(4, "test", null, true, "passed"));
        await TransferRestore.ApplyAsync(Json(corrected), corrected.BaseCommit, workspace, tools, default);
        Assert.Equal("42", await File.ReadAllTextAsync(workspace.Resolve("answer.txt")));
        await File.WriteAllTextAsync(workspace.Resolve("answer.txt"), "41");
        var unresolved = Transfer(new(1, "write", "answer.txt", true, "file_updated", Hash("42")), new(2, "test", null, false, failed));
        await Assert.ThrowsAsync<InvalidDataException>(() => TransferRestore.ApplyAsync(Json(unresolved), unresolved.BaseCommit, workspace, tools, default));
        var timedOut = Transfer(new(1, "write", "answer.txt", true, "file_updated", Hash("42")),
            new(2, "test", null, false, "{\"ExitCode\":1,\"TimedOut\":true,\"OutputTruncated\":false}"), new(3, "test", null, true, "passed"));
        await Assert.ThrowsAsync<InvalidDataException>(() => TransferRestore.ApplyAsync(Json(timedOut), timedOut.BaseCommit, workspace, tools, default));
    }
    private sealed class FinishOnly : IAgentModel { public Task<AgentAction> NextAsync(PortableContext context, CancellationToken ct) => Task.FromResult(new AgentAction("finish")); }
}
