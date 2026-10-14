using TeamFlow.Api.Features.Memory;
using TeamFlow.Api.Features.Integrations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Auth;
using TeamFlow.Api.Features.Tasks;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Executions;

public sealed class ExecutionScheduler(AppDbContext db, TaskAccess tasks, TimeProvider clock, RepositoryAccess repositories, ProfileAccess profiles, PlanningAccess planning, MemorySearch memory, ExecutionTransfer transfer, LocalCliAgents agents, LocalSkills skills)
{
    private static readonly JsonSerializerOptions SnapshotOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static ExecutionDto ToDto(Execution execution) => new(execution.Id, execution.TaskId, execution.State, execution.Attempt,
        execution.MaxAttempts, execution.MaxDurationSeconds, execution.MaxSteps, execution.MaxCostUsd, execution.CreatedAt,
        execution.CancellationRequested, execution.FailureCode, execution.ParentExecutionId, execution.SkillName);

    public async Task<ExecutionDto> ScheduleAsync(Guid organizationId, Guid projectId, Guid taskId, ExecutionInput request,
        string? idempotencyKey, ClaimsPrincipal user, CancellationToken ct, RepositoryAccess.StackedBase? stackedOn = null)
    {
        var task = await tasks.RequireAsync(organizationId, projectId, taskId, user, ct);
        var key = Input.Required(idempotencyKey, "Idempotency-Key", 128);
        if (request.MaxDurationSeconds is < 30 or > 3600 || request.MaxAttempts is < 1 or > 5
            || request.MaxSteps is < 1 or > 200 || request.MaxCostUsd is < 0.01m or > 100m
            || request.MaxCostUsd != decimal.Round(request.MaxCostUsd, 2))
            throw new ApiException(400, "invalid_execution_limits", "Execution limits exceed supported bounds.");
        // Preserve hashes of commands issued before the optional profile field existed.
        var payload = JsonSerializer.Serialize(new { request.Version, request.MaxDurationSeconds, request.MaxAttempts, request.MaxSteps, request.MaxCostUsd });
        if (request.AgentProfileId is not null) payload += "|" + request.AgentProfileId.Value.ToString("D");
        if (request.ParentExecutionId is not null) payload += "|" + JsonSerializer.Serialize(new { request.ParentExecutionId, request.Feedback });
        else if (request.Feedback is not null) throw new ApiException(400, "feedback_parent_required", "Feedback requires a parent execution.");
        if (request.Skill is not null) payload += "|skill:" + request.Skill;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        // A task may be saved before its AI is available; running it needs the CLI installed, logged in and enabled.
        // Checked before the transaction: it starts a local process, which must never run while a transaction is open.
        var chosenProfile = request.AgentProfileId ?? task.AgentProfileId;
        var provider = chosenProfile is null ? null
            : await db.AgentProfiles.AsNoTracking().Where(p => p.Id == chosenProfile && p.IsTaskSelection).Select(p => p.Provider).SingleOrDefaultAsync(ct);
        if (provider is not null) await agents.RequireReadyAsync(provider, ct);
        // Reads one small file from the task folder or the user profile; it is stored with the execution, never re-read later.
        var chosenSkill = request.Skill is null ? null : skills.Resolve(organizationId, projectId, task.FolderPath, request.Skill);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({taskId.ToString()}))", ct);
        var previous = await db.Executions.AsNoTracking().SingleOrDefaultAsync(e => e.OrganizationId == organizationId && e.TaskId == taskId && e.IdempotencyKey == key, ct);
        if (previous is not null)
        {
            if (previous.RequestHash != hash)
                throw new ApiException(409, "idempotency_conflict", "This key was already used with a different request.");
            return ToDto(previous);
        }
        await db.Entry(task).ReloadAsync(ct);
        TaskRules.RequireVersion(task, request.Version);
        var selectedProfile = request.AgentProfileId ?? task.AgentProfileId;
        Execution? parentExecution = null;
        if (request.ParentExecutionId is { } parentId)
        {
            var parent = await db.Executions.AsNoTracking().SingleOrDefaultAsync(e => e.Id == parentId && e.OrganizationId == organizationId
                && e.ProjectId == projectId && e.TaskId == taskId && e.PublicationState == "published", ct)
                ?? throw new ApiException(404, "feedback_parent_not_found", "Parent execution is not a published execution of this task.");
            var latest = await db.Executions.Where(e => e.TaskId == taskId && e.OrganizationId == organizationId && e.ProjectId == projectId)
                .OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id).Select(e => e.Id).FirstAsync(ct);
            if (latest != parentId || parent.ReviewState is "merged" or "merge_policy_unverified")
                throw new ApiException(409, "feedback_parent_superseded", "Feedback requires the latest unmerged review.");
            // Later steps were built on this step's commit: changing it would leave them on a result that no longer exists.
            if (await db.WorkItems.AnyAsync(t => t.PreviousTaskId == taskId && db.Executions.Any(e => e.TaskId == t.Id), ct))
                throw new ApiException(409, "next_step_started", "The next step of the sequence already started; correct this step before running the next one.");
            // A correction of a sequence step keeps the base it was built on.
            if (JsonSerializer.Deserialize<RepositorySnapshot>(parent.RepositorySnapshot ?? "null", SnapshotOptions) is { StackedOn: { } stackedParent } parentSnapshot)
                stackedOn = new(stackedParent, parentSnapshot.BaseCommit);
            TaskAccess.ApplyFeedback(task, request.Feedback ?? "", parent.PullRequestUrl ?? throw new InvalidDataException("Missing parent PR."));
            selectedProfile ??= parent.AgentProfileId;
            parentExecution = parent;
        }
        if (task.Stage != TaskStage.Ready || task.AcceptanceCriteria.Length == 0)
            throw new ApiException(409, "task_not_ready", "Task must be ready with acceptance criteria.");
        await tasks.RequireDependenciesCompleteAsync(task, ct);
        // Running a continuation alone would fork from the branch tip and miss the previous step's work; the sequence runs it on top of that work.
        if (task.PreviousTaskId is { } before && stackedOn is null && await db.WorkItems.AnyAsync(t => t.Id == before && t.Stage != TaskStage.Done, ct))
            throw new ApiException(409, "previous_step_pending", "Run this task through its sequence, or conclude the previous step first.");
        if (await db.Executions.AnyAsync(e => e.TaskId == taskId && (e.State == ExecutionState.Queued || e.State == ExecutionState.Leased), ct))
            throw new ApiException(409, "execution_active", "Task already has an active execution.");
        if (selectedProfile is { } profileId) await profiles.RequireActiveAsync(organizationId, projectId, profileId, ct);
        var previousCreatedAt = await db.Executions.Where(e => e.OrganizationId == organizationId && e.ProjectId == projectId && e.TaskId == taskId)
            .MaxAsync(e => (DateTimeOffset?)e.CreatedAt, ct);
        var createdAt = clock.GetUtcNow();
        // PostgreSQL timestamps have microsecond precision; the task lock makes this ordering stable even with a frozen clock.
        if (previousCreatedAt is { } previousTime && createdAt <= previousTime.AddTicks(10)) createdAt = previousTime.AddTicks(10);
        var execution = new Execution
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ProjectId = projectId,
            TaskId = taskId,
            RequestedBy = AuthEndpoints.UserId(user),
            IdempotencyKey = key,
            RequestHash = hash,
            TaskSnapshot = JsonSerializer.Serialize(TaskDto.From(task), SnapshotOptions),
            CreatedAt = createdAt,
            MaxDurationSeconds = request.MaxDurationSeconds,
            MaxAttempts = request.MaxAttempts,
            MaxSteps = request.MaxSteps,
            MaxCostUsd = request.MaxCostUsd,
            AgentProfileId = selectedProfile,
            ParentExecutionId = request.ParentExecutionId,
            ReviewFeedback = request.Feedback,
            // A correction keeps the skill of the run it fixes unless another one is chosen.
            SkillName = chosenSkill?.Name ?? parentExecution?.SkillName,
            SkillSnapshot = chosenSkill is null ? parentExecution?.SkillSnapshot : JsonSerializer.Serialize(chosenSkill, SnapshotOptions)
        };
        execution.RepositorySnapshot = await repositories.SnapshotAsync(organizationId, projectId, execution.Id, ct, task.RepositoryConnectionId, stackedOn);
        if (task.AnalysisId is { } analysisId)
            execution.MemoryContext = JsonSerializer.Serialize(await planning.ContextAsync(organizationId, projectId, analysisId, user, ct), MemorySearch.JsonOptions);
        else if (parentExecution?.MemoryContext is { } inheritedMemory)
        {
            var bundle = JsonSerializer.Deserialize<ContextBundle>(inheritedMemory, MemorySearch.JsonOptions) ?? throw new InvalidDataException("Invalid inherited sources.");
            await memory.VerifyAsync(organizationId, projectId, bundle, user, ct);
            execution.MemoryContext = inheritedMemory;
        }
        if (parentExecution is not null) execution.TransferSnapshot = await transfer.BuildAsync(parentExecution, execution.RepositorySnapshot, ct);
        if (Encoding.UTF8.GetByteCount(execution.TaskSnapshot) + Encoding.UTF8.GetByteCount(execution.MemoryContext ?? "")
            + Encoding.UTF8.GetByteCount(execution.TransferSnapshot ?? "") > 96 * 1024)
            throw new ApiException(413, "transfer_context_limit", "Scheduled portable context exceeds 96 KiB.");
        db.Executions.Add(execution);
        task.Stage = TaskStage.Execution;
        task.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ToDto(execution);
    }

    public async Task<ExecutionLease?> ClaimAsync(Guid workerId, int leaseSeconds, CancellationToken ct, Guid? organizationId = null, Guid? projectId = null, Guid? runnerCredentialId = null)
    {
        if (workerId == Guid.Empty || leaseSeconds is < 5 or > 120)
            throw new ArgumentException("Worker identity and a lease of 5..120 seconds are required.");
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (runnerCredentialId is not null)
        {
            // Serialize admission against revocation without holding a transaction during sandbox work.
            var credentials = await db.RunnerCredentials.FromSqlInterpolated($"""
                SELECT * FROM "RunnerCredentials" WHERE "Id" = {runnerCredentialId} AND "OrganizationId" = {organizationId}
                AND "ProjectId" = {projectId} AND "WorkerId" = {workerId}
                AND "RevokedAt" IS NULL AND "ExpiresAt" > {now} FOR SHARE
                """).ToListAsync(ct);
            if (credentials.Count != 1) throw new ApiException(401, "runner_revoked", "Runner credential is no longer valid.");
        }
        var exhausted = await db.Executions.FromSqlInterpolated($"""
            SELECT * FROM "Executions" WHERE "State" = 'Leased' AND "LeaseExpiresAt" <= {now}
            AND ("CancellationRequested" OR "Attempt" >= "MaxAttempts" OR "DeadlineAt" <= {now})
            AND ({organizationId}::uuid IS NULL OR "OrganizationId" = {organizationId})
            AND ({projectId}::uuid IS NULL OR "ProjectId" = {projectId})
            ORDER BY "CreatedAt", "Id" LIMIT 100 FOR UPDATE SKIP LOCKED
            """).ToListAsync(ct);
        foreach (var execution in exhausted)
        {
            execution.State = execution.CancellationRequested ? ExecutionState.Cancelled : ExecutionState.Failed;
            execution.FailureCode = execution.CancellationRequested ? null : "execution_limit_reached";
            execution.LeaseToken = null;
            await tasks.ApplyExecutionEndAsync(execution.OrganizationId, execution.ProjectId, execution.TaskId, execution.CancellationRequested, ct);
        }
        await db.SaveChangesAsync(ct);
        var available = await db.Executions.FromSqlInterpolated($"""
            SELECT * FROM "Executions" WHERE NOT "CancellationRequested" AND "Attempt" < "MaxAttempts"
            AND ("DeadlineAt" IS NULL OR "DeadlineAt" > {now})
            AND ({organizationId}::uuid IS NULL OR "OrganizationId" = {organizationId})
            AND ({projectId}::uuid IS NULL OR "ProjectId" = {projectId})
            AND ("State" = 'Queued' OR ("State" = 'Leased' AND "LeaseExpiresAt" <= {now}))
            ORDER BY "CreatedAt", "Id" LIMIT 1 FOR UPDATE SKIP LOCKED
            """).ToListAsync(ct);
        var claimed = available.SingleOrDefault();
        if (claimed is null) { await transaction.CommitAsync(ct); return null; }
        if (claimed.MemoryContext is { } memoryJson)
        {
            try
            {
                var bundle = JsonSerializer.Deserialize<ContextBundle>(memoryJson, MemorySearch.JsonOptions) ?? throw new InvalidDataException("Invalid scheduled sources.");
                var requester = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, claimed.RequestedBy.ToString())], "scheduled-context"));
                await memory.VerifyAsync(claimed.OrganizationId, claimed.ProjectId, bundle, requester, ct);
            }
            catch (ApiException error) when (error.Status is 403 or 404 or 409)
            {
                claimed.State = ExecutionState.Failed; claimed.FailureCode = "context_source_changed";
                await tasks.ApplyExecutionEndAsync(claimed.OrganizationId, claimed.ProjectId, claimed.TaskId, false, ct);
                await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return null;
            }
        }
        claimed.State = ExecutionState.Leased;
        claimed.Attempt++;
        claimed.WorkerId = workerId;
        claimed.RunnerCredentialId = runnerCredentialId;
        claimed.LeaseToken = Guid.NewGuid();
        claimed.DeadlineAt ??= now.AddSeconds(claimed.MaxDurationSeconds);
        claimed.LeaseExpiresAt = new[] { now.AddSeconds(leaseSeconds), claimed.DeadlineAt.Value }.Min();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new ExecutionLease(claimed.Id, workerId, claimed.LeaseToken.Value, claimed.LeaseExpiresAt.Value,
            claimed.DeadlineAt.Value, claimed.Attempt, claimed.TaskSnapshot, claimed.MaxSteps, claimed.MaxCostUsd);
    }

    public async Task<bool> RenewAsync(Guid executionId, Guid workerId, Guid token, int leaseSeconds, CancellationToken ct, Guid? runnerCredentialId = null)
    {
        if (leaseSeconds is < 5 or > 120) throw new ArgumentOutOfRangeException(nameof(leaseSeconds));
        var now = clock.GetUtcNow();
        var desired = now.AddSeconds(leaseSeconds);
        var updated = await db.Executions.Where(e => e.Id == executionId && e.State == ExecutionState.Leased
            && e.RunnerCredentialId == runnerCredentialId
            && (runnerCredentialId == null || db.RunnerCredentials.Any(r => r.Id == runnerCredentialId
                && r.OrganizationId == e.OrganizationId && r.ProjectId == e.ProjectId && r.RevokedAt == null && r.ExpiresAt > now))
            && e.WorkerId == workerId && e.LeaseToken == token && e.LeaseExpiresAt > now && e.DeadlineAt > now && !e.CancellationRequested)
            .ExecuteUpdateAsync(set => set.SetProperty(e => e.LeaseExpiresAt, e => e.DeadlineAt < desired ? e.DeadlineAt : desired), ct);
        return updated == 1;
    }

    public async Task<ExecutionDto> CancelAsync(Guid organizationId, Guid projectId, Guid taskId, Guid executionId, ClaimsPrincipal user, CancellationToken ct)
    {
        await tasks.RequireAsync(organizationId, projectId, taskId, user, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var matches = await db.Executions.FromSqlInterpolated($"""
            SELECT * FROM "Executions" WHERE "Id" = {executionId} AND "OrganizationId" = {organizationId}
            AND "ProjectId" = {projectId} AND "TaskId" = {taskId} FOR UPDATE
            """).ToListAsync(ct);
        var execution = matches.SingleOrDefault() ?? throw new ApiException(404, "execution_not_found", "Execution not found.");
        if (execution.State is ExecutionState.Queued or ExecutionState.Leased)
        {
            execution.CancellationRequested = true;
            if (execution.State == ExecutionState.Queued)
            {
                execution.State = ExecutionState.Cancelled;
                await tasks.ApplyExecutionEndAsync(organizationId, projectId, taskId, cancelled: true, ct);
            }
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return ToDto(execution);
    }
}
