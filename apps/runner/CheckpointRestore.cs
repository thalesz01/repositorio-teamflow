using System.Text.Json;
using System.Security.Cryptography;
using TeamFlow.Runner;

namespace TeamFlow.Runner;

public sealed record RecoveryWrite(int Step, string Path, string Content, string ExpectedHash, string Hash);
public sealed record RemoteRecovery(RecoveryCheckpoint? Checkpoint, RecoveryWrite[] Writes);
public sealed record RecoveryCheckpoint(int Attempt, int Sequence, string ContentHash, DateTimeOffset CreatedAt, PortableContext Context);

public static class CheckpointRestore
{
    public static async Task<PortableContext> RestoreAsync(PortableContext initial, RemoteRecovery recovery,
        ExecutionWorkspace workspace, WorkspaceTools tools, CancellationToken ct)
    {
        if (recovery.Checkpoint is not { } checkpoint) return initial;
        var saved = checkpoint.Context;
        if (saved.ExecutionId != initial.ExecutionId || saved.SchemaVersion != 1 || saved.BaseCommit != initial.BaseCommit
            || saved.Objective != initial.Objective || !saved.AcceptanceCriteria.SequenceEqual(initial.AcceptanceCriteria) || !Matches(saved.Memory, initial.Memory) || !Matches(saved.History, initial.History))
            throw new InvalidDataException("Recovery context does not match scheduled work.");
        foreach (var write in recovery.Writes.OrderBy(w => w.Step))
        {
            var path = workspace.Resolve(write.Path);
            var existing = File.Exists(path) ? Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path, ct))) : "missing";
            if (existing == write.Hash) continue;
            if (existing != write.ExpectedHash) throw new InvalidDataException("Recovery file hash conflict.");
            var restored = await tools.ExecuteAsync(write.Step, new("write", write.Path, write.Content, write.ExpectedHash), ct);
            if (!restored.Succeeded || restored.Hash != write.Hash) throw new InvalidDataException("Recovery write failed.");
        }
        if (saved.Evidence.Any(e => !e.Succeeded && !TestEvidence.IsRecoverable(e))) return saved;
        if (TestEvidence.IsValidated(saved))
        {
            var verified = await tools.ExecuteAsync(saved.Evidence.Length + 1, new("test"), ct);
            if (!verified.Succeeded || !TestEvidence.SameValidation(saved.Evidence.Last(e => e.Tool == "test" && e.Succeeded).Detail, verified.Detail)) throw new InvalidDataException("Recovered checkout failed validation.");
        }
        return saved;
    }
    private static bool Matches(JsonElement? a, JsonElement? b) => a is null ? b is null : b is { } value && JsonElement.DeepEquals(a.Value, value);
}
