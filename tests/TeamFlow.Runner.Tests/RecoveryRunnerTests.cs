using System.Security.Cryptography;
using System.Text;
using TeamFlow.Runner;
using Xunit;

namespace TeamFlow.Runner.Tests;

public sealed class RecoveryRunnerTests
{
    [Fact]
    public async Task ConfirmedWritesAreRestoredAndRepeatedRestorationDoesNotRewriteAsync()
    {
        using var workspace = new ExecutionWorkspace(Path.GetFullPath("work/recovery"));
        await File.WriteAllTextAsync(workspace.Resolve("answer.txt"), "41");
        var initial = new PortableContext(1, Guid.NewGuid(), "Goal", ["Answer is 42"], new string('a', 40), [], []);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("42")));
        var original = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("41")));
        var saved = initial with { Evidence = [new(1, "write", "answer.txt", true, "file_updated", hash)] };
        var recovery = new RemoteRecovery(new(1, 1, "fixture", DateTimeOffset.UtcNow, saved), [new(1, "answer.txt", "42", original, hash)]);
        var tools = new WorkspaceTools(workspace, new DockerSandbox(), ["answer.txt"], ["answer.txt"], new("true", [], TimeSpan.FromSeconds(1)));
        Assert.Equal(saved, await CheckpointRestore.RestoreAsync(initial, recovery, workspace, tools, default));
        var writtenAt = File.GetLastWriteTimeUtc(workspace.Resolve("answer.txt"));
        await CheckpointRestore.RestoreAsync(initial, recovery, workspace, tools, default);
        Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(workspace.Resolve("answer.txt")));
        Assert.Equal("42", await File.ReadAllTextAsync(workspace.Resolve("answer.txt")));
        await File.WriteAllTextAsync(workspace.Resolve("answer.txt"), "conflicting change");
        await Assert.ThrowsAsync<InvalidDataException>(() => CheckpointRestore.RestoreAsync(initial, recovery, workspace, tools, default));
    }
}
