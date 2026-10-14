using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TeamFlow.Runner;

public sealed record PortableTransfer(Guid ParentExecutionId, string BaseCommit, string? PublishedHeadCommit,
    PortableContext Context, RecoveryWrite[] Writes);

public static class TransferRestore
{
    public static async Task ApplyAsync(JsonElement? snapshot, string expectedBase, ExecutionWorkspace workspace, WorkspaceTools tools, CancellationToken ct)
    {
        if (snapshot is null) return;
        var transfer = snapshot.Value.Deserialize<PortableTransfer>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Invalid portable transfer.");
        if (transfer.ParentExecutionId == Guid.Empty || transfer.BaseCommit != expectedBase || transfer.Context.BaseCommit != expectedBase
            // A test that failed and was fixed before the last passing test is accepted, exactly as the API accepts it when it builds the transfer.
            || transfer.Writes.Length > 100 || !TestEvidence.IsValidated(transfer.Context))
            throw new InvalidDataException("Transfer does not match the authorized base and confirmed evidence.");
        foreach (var write in transfer.Writes)
        {
            if (Encoding.UTF8.GetByteCount(write.Content) > 16384 || Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(write.Content))) != write.Hash)
                throw new InvalidDataException("Transfer content hash mismatch.");
            var path = workspace.Resolve(write.Path);
            var current = File.Exists(path) ? Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path, ct))) : "missing";
            if (current == write.Hash) continue;
            if (current != write.ExpectedHash) throw new InvalidDataException("Transferred write conflicts with checked out base.");
            var applied = await tools.ExecuteAsync(write.Step, new("write", write.Path, write.Content, write.ExpectedHash), ct);
            if (!applied.Succeeded || applied.Hash != write.Hash) throw new InvalidDataException("Transferred path was not allowed by the controller.");
        }
    }
}
