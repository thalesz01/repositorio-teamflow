using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Tasks;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Executions;

public sealed record ExecutionResultInput(Guid LeaseToken, Guid OperationId, int CheckpointSequence, bool Validated, string Reason);
public sealed record ExecutionResultReply(Guid ExecutionId, Guid OperationId, bool Validated, string Reason, int CheckpointSequence);

public sealed class ExecutionResultAccess(AppDbContext db, LeaseAccess leases, TaskAccess tasks, TimeProvider clock)
{
    public async Task<ExecutionResultReply> SaveAsync(Guid id, ExecutionResultInput input, ClaimsPrincipal user, CancellationToken ct)
    {
        if (input.OperationId == Guid.Empty || input.CheckpointSequence < 0
            || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 64
            || input.Reason.Any(c => c != '_' && (c < 'a' || c > 'z'))
            || input.Validated != (input.Reason is "tests_passed" or "analysis_answered"))
            throw new ApiException(400, "invalid_execution_result", "Unsupported execution result.");
        var execution = await leases.RequireRecordedOwnerAsync(id, input.LeaseToken, user, ct);
        var hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        { input.OperationId, input.CheckpointSequence, input.Validated, input.Reason })));
        if (execution.ResultOperationId is not null) return Replay(execution);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        execution = await leases.LockRecordedOwnerAsync(id, input.LeaseToken, user, ct);
        if (execution.ResultOperationId is not null) return Replay(execution);
        if (execution.State != ExecutionState.Leased) throw new ApiException(409, "lease_lost", "Execution is not leased.");
        var cancelled = input.Reason == "cancelled" && execution.CancellationRequested;
        var expired = input.Reason == "deadline_exceeded" && execution.DeadlineAt <= clock.GetUtcNow();
        if (!cancelled && !expired) await leases.RequireAsync(id, input.LeaseToken, user, ct);
        if ((input.Reason == "cancelled" && !cancelled) || (input.Reason == "deadline_exceeded" && !expired))
            throw new ApiException(409, "invalid_stop_reason", "Stop reason does not match server state.");
        var checkpoint = await db.ExecutionCheckpoints.AsNoTracking().Where(c => c.ExecutionId == id && c.Attempt == execution.Attempt)
            .OrderByDescending(c => c.Sequence).FirstOrDefaultAsync(ct);
        if (input.Validated)
        {
            if (checkpoint is null || checkpoint.Sequence != input.CheckpointSequence) throw InvalidEvidence();
            var context = JsonSerializer.Deserialize<CheckpointContext>(checkpoint.Context, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidDataException("Invalid persisted checkpoint.");
            var kind = await db.WorkItems.AsNoTracking().Where(t => t.Id == execution.TaskId).Select(t => t.Kind).SingleAsync(ct);
            if (input.Reason == "analysis_answered" ? kind != "analysis" || !TestEvidence.IsAnswered(context) || !await HasAnswerAsync(id, ct)
                : kind == "analysis" || !TestEvidence.IsValidated(context)) throw InvalidEvidence();
            if (await db.ModelTurns.AnyAsync(t => t.ExecutionId == id && (t.State != "completed" || (t.BillingMode == "api" && t.EstimatedCostUsd == null)), ct))
                throw new ApiException(409, "model_outcome_uncertain", "Cannot validate an execution with unknown model consumption.");
            if (await db.ModelTurns.Where(t => t.ExecutionId == id).SumAsync(t => t.EstimatedCostUsd ?? t.ReservedUsd, ct) > execution.MaxCostUsd)
                throw new ApiException(409, "budget_exceeded", "Model budget exceeded.");
        }
        else if (input.CheckpointSequence != (checkpoint?.Sequence ?? 0)) throw InvalidEvidence();
        var tracked = await db.Executions.SingleAsync(e => e.Id == id, ct);
        tracked.ResultOperationId = input.OperationId;
        tracked.ResultHash = hash;
        tracked.ResultCode = input.Reason;
        tracked.ResultCheckpointSequence = input.CheckpointSequence;
        tracked.ResultCreatedAt = clock.GetUtcNow();
        tracked.State = cancelled ? ExecutionState.Cancelled : input.Validated ? ExecutionState.Succeeded : ExecutionState.Failed;
        tracked.FailureCode = input.Validated || cancelled ? null : input.Reason;
        if (!input.Validated) await tasks.ApplyExecutionEndAsync(execution.OrganizationId, execution.ProjectId, execution.TaskId, cancelled, ct);
        else if (input.Reason == "analysis_answered") await tasks.ApplyAnalysisAnsweredAsync(execution.OrganizationId, execution.ProjectId, execution.TaskId, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        // Success is the harness result. The task remains Execution until PR publication/review workflow.
        return new(id, input.OperationId, input.Validated, input.Reason, input.CheckpointSequence);

        ExecutionResultReply Replay(Execution saved)
        {
            if (saved.ResultOperationId != input.OperationId || saved.ResultHash != hash)
                throw new ApiException(409, "result_conflict", "Execution already has a different result.");
            return new(id, input.OperationId, saved.State == ExecutionState.Succeeded,
                saved.ResultCode ?? throw new InvalidDataException("Missing result code."), saved.ResultCheckpointSequence ?? 0);
        }
    }

    // The answer is the content of the closing "finish" action; an analysis without one has produced nothing to show.
    private async Task<bool> HasAnswerAsync(Guid id, CancellationToken ct)
    {
        var last = await db.ModelTurns.AsNoTracking().Where(t => t.ExecutionId == id && t.State == "completed")
            .OrderByDescending(t => t.Attempt).ThenByDescending(t => t.Sequence).Select(t => t.Action).FirstOrDefaultAsync(ct);
        if (last is null) return false;
        try
        {
            using var action = JsonDocument.Parse(last);
            return action.RootElement.TryGetProperty("tool", out var tool) && tool.GetString() == "finish"
                && action.RootElement.TryGetProperty("content", out var content) && !string.IsNullOrWhiteSpace(content.GetString());
        }
        catch (JsonException) { return false; }
    }

    private static ApiException InvalidEvidence() => new(409, "validation_missing", "Result lacks the required current checkpoint evidence.");
}
