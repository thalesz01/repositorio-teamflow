using TeamFlow.Api.Features.Memory;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Integrations;
using TeamFlow.Api.Features.Tasks;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Executions;

public sealed class CheckpointAccess(AppDbContext db, LeaseAccess leases, TimeProvider clock, MemorySearch memory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) } };
    public const int MaxContextBytes = 96 * 1024;

    public async Task<CheckpointReply> SaveAsync(Guid id, CheckpointInput input, ClaimsPrincipal user, CancellationToken ct)
    {
        var execution = await leases.RequireAsync(id, input.LeaseToken, user, ct);
        await VerifyMemoryAsync(execution, ct);
        var json = Validate(execution, input.Context);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        var sequence = input.Context.Evidence.Length;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Same lock order as claim: credential, then execution. No external I/O inside this transaction.
        await db.RunnerCredentials.FromSqlInterpolated($"""
            SELECT * FROM "RunnerCredentials" WHERE "Id" = {execution.RunnerCredentialId} FOR SHARE
            """).AsNoTracking().ToListAsync(ct);
        await db.Executions.FromSqlInterpolated($"""
            SELECT * FROM "Executions" WHERE "Id" = {id} AND "OrganizationId" = {execution.OrganizationId}
            AND "ProjectId" = {execution.ProjectId} FOR UPDATE
            """).AsNoTracking().ToListAsync(ct);
        execution = await leases.RequireAsync(id, input.LeaseToken, user, ct);
        var query = db.ExecutionCheckpoints.AsNoTracking().Where(c => c.ExecutionId == id
            && c.OrganizationId == execution.OrganizationId && c.ProjectId == execution.ProjectId && c.Attempt == execution.Attempt);
        var existing = await query.SingleOrDefaultAsync(c => c.Sequence == sequence, ct);
        if (existing is not null)
        {
            if (existing.ContentHash != hash) throw Conflict();
            await transaction.CommitAsync(ct);
            return Reply(existing);
        }
        var previous = await query.OrderByDescending(c => c.Sequence).FirstOrDefaultAsync(ct);
        var inherited = previous is null ? await db.ExecutionCheckpoints.AsNoTracking().Where(c => c.ExecutionId == id
            && c.Attempt < execution.Attempt).OrderByDescending(c => c.Attempt).ThenByDescending(c => c.Sequence).FirstOrDefaultAsync(ct) : null;
        if (previous is null && inherited is not null)
        {
            if (sequence != inherited.Sequence || hash != inherited.ContentHash) throw Conflict();
        }
        else if (sequence != (previous?.Sequence + 1 ?? 0)) throw Conflict();
        if (previous is not null)
        {
            var saved = JsonSerializer.Deserialize<CheckpointContext>(previous.Context, JsonOptions)
                ?? throw new InvalidDataException("Invalid persisted checkpoint.");
            if (saved.Evidence.Any(e => !e.Succeeded) || !saved.Evidence.SequenceEqual(input.Context.Evidence.Take(sequence - 1))
                || !saved.Pending.SequenceEqual(input.Context.Pending)) throw Conflict();
        }
        var checkpoint = new ExecutionCheckpoint
        {
            ExecutionId = id,
            OrganizationId = execution.OrganizationId,
            ProjectId = execution.ProjectId,
            Attempt = execution.Attempt,
            Sequence = sequence,
            Context = json,
            ContentHash = hash,
            CreatedAt = clock.GetUtcNow()
        };
        db.ExecutionCheckpoints.Add(checkpoint);
        await db.SaveChangesAsync(ct);
        await leases.RequireAsync(id, input.LeaseToken, user, ct);
        await transaction.CommitAsync(ct);
        return Reply(checkpoint);
    }

    public async Task<CheckpointReply?> ReadAsync(Guid id, Guid token, ClaimsPrincipal user, CancellationToken ct)
    {
        var execution = await leases.RequireAsync(id, token, user, ct);
        await VerifyMemoryAsync(execution, ct);
        var checkpoint = await db.ExecutionCheckpoints.AsNoTracking().Where(c => c.ExecutionId == id
            && c.OrganizationId == execution.OrganizationId && c.ProjectId == execution.ProjectId)
            .OrderByDescending(c => c.Attempt).ThenByDescending(c => c.Sequence).FirstOrDefaultAsync(ct);
        await leases.RequireAsync(id, token, user, ct);
        return checkpoint is null ? null : Reply(checkpoint);
    }

    private static string Validate(Execution execution, CheckpointContext context)
    {
        if (context is null || context.SchemaVersion != 1 || context.ExecutionId != execution.Id
            || context.AcceptanceCriteria is null || context.Evidence is null || context.Pending is null
            || context.Evidence.Length > execution.MaxSteps || context.Pending.Length > 30
            || context.Pending.Any(p => string.IsNullOrWhiteSpace(p) || p.Length > 1000)) throw Invalid();
        var task = JsonSerializer.Deserialize<TaskDto>(execution.TaskSnapshot, JsonOptions)
            ?? throw new InvalidDataException("Invalid task snapshot.");
        var repository = execution.RepositorySnapshot is null ? null
            : JsonSerializer.Deserialize<RepositorySnapshot>(execution.RepositorySnapshot, JsonOptions);
        if (repository is null || context.Objective != task.Title + "\n" + task.Description
            || context.BaseCommit != repository.BaseCommit || !context.AcceptanceCriteria.SequenceEqual(task.AcceptanceCriteria)) throw Invalid();
        for (var i = 0; i < context.Evidence.Length; i++)
        {
            var evidence = context.Evidence[i];
            if (evidence is null || evidence.Step != i + 1 || evidence.Tool is not ("read" or "write" or "test" or "rejected")
                || evidence.Detail is null || Encoding.UTF8.GetByteCount(evidence.Detail) > 16384
                || (evidence.Path is not null && (evidence.Path.Length > 240 || evidence.Path.Contains(':')
                    || evidence.Path.StartsWith('/') || evidence.Path.StartsWith('\\')
                    || evidence.Path.Split('/', '\\').Any(p => p.StartsWith('.') || p.Length == 0)))
                || (evidence.Hash is not null && (evidence.Hash.Length != 64 || !evidence.Hash.All(Uri.IsHexDigit)))) throw Invalid();
        }
        if (!Matches(context.Memory, execution.MemoryContext) || !Matches(context.History, execution.TransferSnapshot)) throw Invalid();
        var json = JsonSerializer.Serialize(context, JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaxContextBytes)
            throw new ApiException(413, "checkpoint_too_large", "Checkpoint context exceeds 96 KiB.");
        return json;
    }

    private async Task VerifyMemoryAsync(Execution execution, CancellationToken ct)
    {
        if (execution.MemoryContext is not { } json) return;
        var bundle = JsonSerializer.Deserialize<ContextBundle>(json, MemorySearch.JsonOptions) ?? throw new InvalidDataException("Invalid execution sources.");
        var requester = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, execution.RequestedBy.ToString())], "checkpoint-context"));
        await memory.VerifyAsync(execution.OrganizationId, execution.ProjectId, bundle, requester, ct);
    }
    private static bool Matches(JsonElement? actual, string? expected) => expected is null ? actual is null
        : actual is { } value && JsonElement.DeepEquals(value, JsonSerializer.Deserialize<JsonElement>(expected));
    private static CheckpointReply Reply(ExecutionCheckpoint checkpoint) => new(checkpoint.Attempt, checkpoint.Sequence,
        checkpoint.ContentHash, checkpoint.CreatedAt, JsonSerializer.Deserialize<CheckpointContext>(checkpoint.Context, JsonOptions)
            ?? throw new InvalidDataException("Invalid persisted checkpoint."));
    private static ApiException Invalid() => new(400, "invalid_checkpoint", "Checkpoint does not match the execution contract.");
    private static ApiException Conflict() => new(409, "checkpoint_conflict", "Checkpoint sequence or contents conflict with saved evidence.");
}
