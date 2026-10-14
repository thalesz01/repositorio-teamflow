using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Integrations;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Executions;

public sealed record PortableTransfer(Guid ParentExecutionId, string BaseCommit, string? PublishedHeadCommit,
    CheckpointContext Context, RecoveryWrite[] Writes);

public sealed class ExecutionTransfer(AppDbContext db)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public async Task<string> BuildAsync(Execution parent, string? repositorySnapshot, CancellationToken ct)
    {
        var oldRepository = JsonSerializer.Deserialize<RepositorySnapshot>(parent.RepositorySnapshot ?? "null", JsonOptions);
        var repository = JsonSerializer.Deserialize<RepositorySnapshot>(repositorySnapshot ?? "null", JsonOptions);
        if (oldRepository is null || repository is null || oldRepository.BaseCommit != repository.BaseCommit
            || oldRepository.ConnectionId != repository.ConnectionId || oldRepository.ConnectionVersion != repository.ConnectionVersion
            || oldRepository.RepositoryId != repository.RepositoryId || oldRepository.RemoteUrl != repository.RemoteUrl || oldRepository.BaseBranch != repository.BaseBranch)
            throw new ApiException(409, "transfer_base_changed", "Transfer requires the same authorized repository and base; reconcile changed bases explicitly.");
        var checkpoint = await db.ExecutionCheckpoints.AsNoTracking().SingleOrDefaultAsync(c => c.ExecutionId == parent.Id
            && c.OrganizationId == parent.OrganizationId && c.ProjectId == parent.ProjectId && c.Attempt == parent.Attempt
            && c.Sequence == parent.ResultCheckpointSequence, ct)
            ?? throw new ApiException(409, "transfer_evidence_missing", "Parent's confirmed portable context is unavailable.");
        var context = JsonSerializer.Deserialize<CheckpointContext>(checkpoint.Context, JsonOptions)
            ?? throw new InvalidDataException("Invalid parent context.");
        context = context with { Memory = null, History = null };
        if (context.BaseCommit != repository.BaseCommit || context.Pending.Length != 0 || !TestEvidence.IsValidated(context))
            throw new ApiException(409, "transfer_evidence_missing", "Parent portable evidence is incomplete.");
        var receipts = await db.ModelTurns.AsNoTracking().Where(t => t.ExecutionId == parent.Id && t.OrganizationId == parent.OrganizationId
            && t.ProjectId == parent.ProjectId).OrderByDescending(t => t.Attempt).Take(200).ToListAsync(ct);
        if (receipts.Any(t => t.State != ModelTurnState.Completed || (t.BillingMode == BillingMode.Api && t.EstimatedCostUsd == null)))
            throw new ApiException(409, "model_outcome_uncertain", "Uncertain parent operations cannot be transferred.");
        var writes = parent.TransferSnapshot is null ? new List<RecoveryWrite>()
            : (JsonSerializer.Deserialize<PortableTransfer>(parent.TransferSnapshot, JsonOptions)?.Writes ?? []).ToList();
        foreach (var evidence in context.Evidence.Where(e => e.Tool == "write" && e.Succeeded))
        {
            var action = receipts.Where(t => t.Sequence == evidence.Step - 1 && t.Action != null)
                .Select(t => JsonSerializer.Deserialize<ModelAction>(t.Action ?? "null", JsonOptions))
                .FirstOrDefault(a => a?.Tool == "write" && a.Path == evidence.Path && a.Content is not null
                    && Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(a.Content))) == evidence.Hash);
            if (action?.Content is null || action.Path is null || action.ExpectedHash is null || evidence.Hash is null)
                throw new ApiException(409, "transfer_evidence_missing", "Parent write does not have a confirmed receipt.");
            writes.Add(new(evidence.Step, action.Path, action.Content, action.ExpectedHash, evidence.Hash));
        }
        var final = new List<RecoveryWrite>();
        foreach (var group in writes.GroupBy(w => w.Path))
        {
            var chain = group.ToArray();
            for (var i = 1; i < chain.Length; i++)
                if (chain[i].ExpectedHash != chain[i - 1].Hash) throw new ApiException(409, "transfer_hash_conflict", "Parent write chain is inconsistent.");
            final.Add(chain[^1] with { ExpectedHash = chain[0].ExpectedHash });
        }
        var json = JsonSerializer.Serialize(new PortableTransfer(parent.Id, repository.BaseCommit, parent.PublishedHeadCommit, context, final.ToArray()), JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > 64 * 1024) throw new ApiException(413, "transfer_context_limit", "Portable transfer exceeds 64 KiB; reconcile manually.");
        return json;
    }
}

