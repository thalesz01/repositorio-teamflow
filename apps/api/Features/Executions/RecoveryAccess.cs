using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Executions;

public sealed record RecoveryInput(Guid LeaseToken);
public sealed record RecoveryWrite(int Step, string Path, string Content, string ExpectedHash, string Hash);
public sealed record RecoveryReply(CheckpointReply? Checkpoint, RecoveryWrite[] Writes);

public sealed class RecoveryAccess(AppDbContext db, LeaseAccess leases, CheckpointAccess checkpoints)
{
    public async Task<RecoveryReply> ReadAsync(Guid id, Guid token, ClaimsPrincipal user, CancellationToken ct)
    {
        var execution = await leases.RequireAsync(id, token, user, ct);
        if (await db.ModelTurns.AnyAsync(t => t.ExecutionId == id && (t.State != ModelTurnState.Completed || (t.BillingMode == BillingMode.Api && t.EstimatedCostUsd == null)), ct))
            throw new ApiException(409, "model_outcome_uncertain", "An unknown model outcome prevents automatic recovery.");
        var checkpoint = await checkpoints.ReadAsync(id, token, user, ct);
        if (checkpoint is null) return new(null, []);
        var turns = await db.ModelTurns.AsNoTracking().Where(t => t.ExecutionId == id && t.Attempt <= checkpoint.Attempt
            && t.State == ModelTurnState.Completed && t.Action != null).OrderByDescending(t => t.Attempt).Take(200).ToListAsync(ct);
        var writes = new List<RecoveryWrite>();
        foreach (var evidence in checkpoint.Context.Evidence.Where(e => e.Tool == "write" && e.Succeeded))
        {
            var action = turns.Where(t => t.Sequence == evidence.Step - 1)
                .Select(t => JsonSerializer.Deserialize<ModelAction>(t.Action ?? "null", new JsonSerializerOptions(JsonSerializerDefaults.Web)))
                .FirstOrDefault(a => a is not null && a.Tool == "write" && a.Path == evidence.Path && a.Content is not null
                    && Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(a.Content))) == evidence.Hash);
            if (action?.Path is null || action.Content is null || action.ExpectedHash is null || evidence.Hash is null)
                throw new ApiException(409, "recovery_data_missing", "Confirmed write has no recoverable model receipt.");
            writes.Add(new(evidence.Step, action.Path, action.Content, action.ExpectedHash, evidence.Hash));
        }
        await leases.RequireAsync(id, token, user, ct);
        return new(checkpoint, writes.ToArray());
    }
}
