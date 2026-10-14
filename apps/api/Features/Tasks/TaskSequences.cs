using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Auth;
using TeamFlow.Api.Features.Executions;
using TeamFlow.Api.Features.Integrations;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Tasks;

/// <summary>
/// A sequence is a chain of change tasks on the same folder, each created as the continuation of the one before (ADR 0015).
/// Starting it marks the waiting steps; <see cref="AdvanceAsync"/> then runs each step on top of the previous step's published
/// commit as soon as that one is ready for review. Nothing is merged: the person reviews and concludes the steps in order.
/// </summary>
public sealed class TaskSequences(AppDbContext db, ExecutionScheduler scheduler, ILogger<TaskSequences> logger)
{
    /// <summary>The chain the task belongs to, first step first.</summary>
    public async Task<List<WorkItem>> ChainAsync(Guid organizationId, Guid projectId, Guid taskId, CancellationToken ct)
    {
        var linked = await db.WorkItems.AsNoTracking().Where(t => t.OrganizationId == organizationId && t.ProjectId == projectId
            && (t.PreviousTaskId != null || db.WorkItems.Any(n => n.PreviousTaskId == t.Id))).ToListAsync(ct);
        var byId = linked.ToDictionary(t => t.Id);
        if (!byId.TryGetValue(taskId, out var current))
            return [await db.WorkItems.AsNoTracking().SingleOrDefaultAsync(t => t.Id == taskId && t.OrganizationId == organizationId && t.ProjectId == projectId, ct)
                ?? throw new ApiException(404, "task_not_found", "Task not found.")];
        var steps = new List<WorkItem>();
        for (var guard = 0; current.PreviousTaskId is { } before && byId.TryGetValue(before, out var earlier) && guard < 1000; guard++) current = earlier;
        var next = linked.ToLookup(t => t.PreviousTaskId);
        for (WorkItem? step = current; step is not null && steps.Count < 1000; step = next[step.Id].FirstOrDefault()) steps.Add(step);
        return steps;
    }

    public async Task<List<WorkItem>> StartAsync(Guid organizationId, Guid projectId, Guid taskId, ClaimsPrincipal user, CancellationToken ct)
    {
        var chain = await ChainAsync(organizationId, projectId, taskId, ct);
        if (chain.Count < 2) throw new ApiException(409, "sequence_empty", "A sequence needs at least two linked tasks.");
        var waiting = chain.Where(t => t.Stage == TaskStage.Ready).Select(t => t.Id).ToArray();
        if (waiting.Length == 0) throw new ApiException(409, "sequence_empty", "No step of this sequence is waiting to run.");
        await db.WorkItems.Where(t => waiting.Contains(t.Id) && t.Stage == TaskStage.Ready)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.SequenceStartedBy, AuthEndpoints.UserId(user)).SetProperty(t => t.Version, Guid.NewGuid()), ct);
        await AdvanceAsync(chain.Select(t => t.Id).ToArray(), ct);
        return await ChainAsync(organizationId, projectId, taskId, ct);
    }

    public async Task<List<WorkItem>> StopAsync(Guid organizationId, Guid projectId, Guid taskId, CancellationToken ct)
    {
        var chain = await ChainAsync(organizationId, projectId, taskId, ct);
        var ids = chain.Select(t => t.Id).ToArray();
        await db.WorkItems.Where(t => ids.Contains(t.Id) && t.SequenceStartedBy != null)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.SequenceStartedBy, (Guid?)null).SetProperty(t => t.Version, Guid.NewGuid()), ct);
        return await ChainAsync(organizationId, projectId, taskId, ct);
    }

    /// <summary>Schedules every marked step whose previous step is ready. Safe to repeat: each step uses a fixed idempotency key.</summary>
    public async Task<int> AdvanceAsync(Guid[]? only, CancellationToken ct)
    {
        var pending = await db.WorkItems.AsNoTracking().Where(t => t.SequenceStartedBy != null && t.Stage == TaskStage.Ready && (only == null || only.Contains(t.Id)))
            .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id).Take(50).ToListAsync(ct);
        var scheduled = 0;
        foreach (var task in pending)
        {
            RepositoryAccess.StackedBase? stacked = null;
            if (task.PreviousTaskId is { } previousId)
            {
                var previous = await db.WorkItems.AsNoTracking().SingleOrDefaultAsync(t => t.Id == previousId, ct);
                if (previous is null) { await InterruptAsync(task, "the previous step no longer exists", ct); continue; }
                // Still running (or waiting its turn, or being published): nothing to do yet.
                if (previous.Stage == TaskStage.Execution || (previous.Stage == TaskStage.Ready && previous.SequenceStartedBy != null)) continue;
                if (previous.Stage is TaskStage.Review or TaskStage.AwaitingMerge)
                {
                    var last = await db.Executions.AsNoTracking().Where(e => e.TaskId == previous.Id)
                        .OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id).FirstOrDefaultAsync(ct);
                    if (last is not { State: ExecutionState.Succeeded, PublicationState: "published", PublishedHeadCommit: { } head })
                    { await InterruptAsync(task, "the previous step has no published result", ct); continue; }
                    stacked = new(last.Id, head);
                }
                else if (previous.Stage != TaskStage.Done) { await InterruptAsync(task, $"the previous step is {previous.Stage}", ct); continue; }
            }
            try
            {
                var requester = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, task.SequenceStartedBy!.Value.ToString())], "sequence"));
                await scheduler.ScheduleAsync(task.OrganizationId, task.ProjectId, task.Id, new ExecutionInput(task.Version), $"sequence-{task.Id:N}", requester, ct, stacked);
                scheduled++;
            }
            catch (ApiException failure)
            {
                await InterruptAsync(task, $"it could not be scheduled ({failure.Code})", ct);
            }
        }
        return scheduled;
    }

    // A failed, cancelled or unschedulable step stops the sequence: later steps stay ready and are not run on top of nothing.
    private async Task InterruptAsync(WorkItem task, string reason, CancellationToken ct)
    {
        logger.LogWarning("Sequence stopped before task {TaskId}: {Reason}.", task.Id, reason);
        await db.WorkItems.Where(t => t.Id == task.Id && t.SequenceStartedBy != null)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.SequenceStartedBy, (Guid?)null).SetProperty(t => t.Version, Guid.NewGuid()), ct);
    }
}

public sealed class SequenceWorker(IServiceScopeFactory scopes, IHostEnvironment environment, ILogger<SequenceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (environment.IsEnvironment("Testing")) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<TaskSequences>().AdvanceAsync(null, stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception failure) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Sequence polling failed ({Type}); retrying in 30 seconds.", failure.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }
}
