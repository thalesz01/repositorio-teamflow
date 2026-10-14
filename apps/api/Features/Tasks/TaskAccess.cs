using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Projects;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Tasks;

public sealed class TaskAccess(AppDbContext db, ProjectAccess projects)
{
    public async Task<WorkItem> RequireAsync(Guid organizationId, Guid projectId, Guid taskId, ClaimsPrincipal user, CancellationToken ct)
    {
        await projects.RequireAsync(organizationId, projectId, user, ct);
        return await db.WorkItems.SingleOrDefaultAsync(t => t.Id == taskId && t.OrganizationId == organizationId && t.ProjectId == projectId, ct)
            ?? throw new ApiException(404, "task_not_found", "Task not found.");
    }

    public Task<int> ApplyExecutionEndAsync(Guid organizationId, Guid projectId, Guid taskId, bool cancelled, CancellationToken ct) =>
        db.WorkItems.Where(t => t.Id == taskId && t.OrganizationId == organizationId && t.ProjectId == projectId && t.Stage == TaskStage.Execution)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.Stage, cancelled ? TaskStage.Cancelled : TaskStage.Failed)
                .SetProperty(t => t.Version, Guid.NewGuid()), ct);
    /// <summary>An analysis has no review or merge: its answer is the result, so a successful execution closes the task.</summary>
    public Task<int> ApplyAnalysisAnsweredAsync(Guid organizationId, Guid projectId, Guid taskId, CancellationToken ct) =>
        db.WorkItems.Where(t => t.Id == taskId && t.OrganizationId == organizationId && t.ProjectId == projectId && t.Stage == TaskStage.Execution && t.Kind == TaskKind.Analysis)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.Stage, TaskStage.Done).SetProperty(t => t.Version, Guid.NewGuid()), ct);

    public Task<int> ApplyPrPublishedAsync(Guid organizationId, Guid projectId, Guid taskId, CancellationToken ct) =>
        db.WorkItems.Where(t => t.Id == taskId && t.OrganizationId == organizationId && t.ProjectId == projectId && t.Stage == TaskStage.Execution)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Stage, TaskStage.Review).SetProperty(t => t.Version, Guid.NewGuid()), ct);

    public Task<int> ApplyReviewStageAsync(Guid organizationId, Guid projectId, Guid taskId, TaskStage stage, CancellationToken ct) =>
        db.WorkItems.Where(t => t.Id == taskId && t.OrganizationId == organizationId && t.ProjectId == projectId
            && (t.Stage == TaskStage.Review || t.Stage == TaskStage.AwaitingMerge))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Stage, stage).SetProperty(t => t.Version, Guid.NewGuid()), ct);

    public async Task<Guid> ReviewVersionAsync(Guid organizationId, Guid projectId, Guid taskId, CancellationToken ct) =>
        await db.WorkItems.AsNoTracking().Where(t => t.Id == taskId && t.OrganizationId == organizationId && t.ProjectId == projectId)
            .Select(t => (Guid?)t.Version).SingleOrDefaultAsync(ct)
            ?? throw new ApiException(404, "task_not_found", "Task not found.");

    public static void ApplyFeedback(WorkItem task, string feedback, string previousPr)
    {
        if (task.Stage is not (TaskStage.Review or TaskStage.AwaitingMerge) || string.IsNullOrWhiteSpace(feedback) || feedback.Length > 6000)
            throw new ApiException(409, "invalid_review_feedback", "Feedback requires a task under review and at most 6000 characters.");
        var description = task.Description + "\n\nReview feedback for " + previousPr + ":\n" + feedback;
        if (description.Length > 10000) throw new ApiException(400, "feedback_context_limit", "Feedback exceeds the task context limit.");
        task.Description = description;
        task.Stage = TaskStage.Ready;
    }

    public async Task RequireDependenciesCompleteAsync(WorkItem task, CancellationToken ct)
    {
        var completed = await db.WorkItems.CountAsync(t => task.DependencyIds.Contains(t.Id)
            && t.OrganizationId == task.OrganizationId && t.ProjectId == task.ProjectId && t.Stage == TaskStage.Done, ct);
        if (completed != task.DependencyIds.Length)
            throw new ApiException(409, "dependencies_pending", "Task dependencies are not completed.");
    }

    public async Task ValidateDependenciesAsync(WorkItem task, Guid[]? dependencyIds, CancellationToken ct)
    {
        if (dependencyIds is null || dependencyIds.Length > 50 || dependencyIds.Distinct().Count() != dependencyIds.Length || dependencyIds.Contains(task.Id))
            throw new ApiException(400, "invalid_dependencies", "Provide at most 50 distinct dependencies, excluding this task.");
        var count = await db.WorkItems.CountAsync(t => dependencyIds.Contains(t.Id) && t.OrganizationId == task.OrganizationId && t.ProjectId == task.ProjectId, ct);
        if (count != dependencyIds.Length)
            throw new ApiException(400, "invalid_dependencies", "Dependencies must belong to this project.");
        var visited = new HashSet<Guid>();
        var pending = dependencyIds.ToList();
        while (pending.Count > 0)
        {
            if (pending.Contains(task.Id))
                throw new ApiException(409, "dependency_cycle", "Task dependencies cannot form a cycle.");
            pending = pending.Where(visited.Add).ToList();
            if (pending.Count == 0) break;
            if (visited.Count > 1000)
                throw new ApiException(400, "dependency_limit", "Dependency graph exceeds the supported limit.");
            var next = await db.WorkItems.Where(t => t.OrganizationId == task.OrganizationId && t.ProjectId == task.ProjectId && pending.Contains(t.Id))
                .Select(t => t.DependencyIds).ToListAsync(ct);
            pending = next.SelectMany(ids => ids).ToList();
        }
    }
}
