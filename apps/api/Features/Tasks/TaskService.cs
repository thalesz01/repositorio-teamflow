using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Integrations;
using TeamFlow.Api.Features.Memory;
using TeamFlow.Api.Features.Organizations;
using TeamFlow.Api.Features.Projects;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Tasks;

public sealed class TaskService(AppDbContext db, ProjectAccess projects, OrganizationAccess organizations, TaskAccess tasks,
    PlanningAccess planning, RepositoryAccess repositories, LocalCliAgents agents)
{
    public async Task<PageResult<TaskDto>> ListAsync(Guid organizationId, Guid projectId, int? page, int? pageSize, ClaimsPrincipal user, CancellationToken ct)
    {
        await projects.RequireAsync(organizationId, projectId, user, ct);
        var (number, size) = Pagination.Validate(page, pageSize);
        var query = db.WorkItems.AsNoTracking().Where(t => t.OrganizationId == organizationId && t.ProjectId == projectId).OrderBy(t => t.CreatedAt).ThenBy(t => t.Id);
        var items = await query.Skip((number - 1) * size).Take(size).ToListAsync(ct);
        return new PageResult<TaskDto>(items.Select(TaskDto.From).ToList(), number, size, await query.CountAsync(ct));
    }

    public async Task<WorkItem> CreateAsync(Guid organizationId, Guid projectId, TaskInput input, ClaimsPrincipal user, CancellationToken ct)
    {
        await projects.RequireAsync(organizationId, projectId, user, ct);
        var task = new WorkItem { Id = Guid.NewGuid(), OrganizationId = organizationId, ProjectId = projectId };
        await ApplyInputAsync(task, input, ct);
        ApplyKind(task, input);
        ApplyInitialStage(task, input.Stage);
        await ApplyFolderAsync(task, input, ct);
        if (input.AnalysisId is { } analysisId)
        {
            await planning.ContextAsync(organizationId, projectId, analysisId, user, ct);
            task.AnalysisId = analysisId;
        }
        if (input.PreviousTaskId is { } previousId) await LinkPreviousAsync(task, previousId, ct);
        if (input.Ai is { } ai) task.AgentProfileId = (await agents.SelectAsync(organizationId, projectId, ai, ct)).Id;
        db.WorkItems.Add(task);
        await db.SaveChangesAsync(ct);
        return task;
    }

    public async Task<WorkItem> UpdateAsync(Guid organizationId, Guid projectId, Guid taskId, TaskUpdate update, ClaimsPrincipal user, CancellationToken ct)
    {
        var task = await tasks.RequireAsync(organizationId, projectId, taskId, user, ct);
        TaskRules.RequireVersion(task, update.Version);
        if (task.Stage is TaskStage.Execution or TaskStage.Review or TaskStage.AwaitingMerge or TaskStage.Done or TaskStage.Cancelled)
            throw new ApiException(409, "task_not_editable", "Task cannot be edited in its current stage.");
        // Serialize graph edits per project so concurrent requests cannot introduce a dependency cycle.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({projectId.ToString()}))", ct);
        await ApplyInputAsync(task, new TaskInput(update.Title, update.Description, update.AcceptanceCriteria, update.AssigneeId, update.DependencyIds), ct);
        task.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return task;
    }

    public async Task<WorkItem> TransitionAsync(Guid organizationId, Guid projectId, Guid taskId, TransitionInput request, ClaimsPrincipal user, CancellationToken ct)
    {
        var task = await tasks.RequireAsync(organizationId, projectId, taskId, user, ct);
        TaskRules.RequireVersion(task, request.Version);
        TaskRules.RequireTransition(task, request.Stage);
        task.Stage = request.Stage;
        task.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return task;
    }

    private async Task ApplyInputAsync(WorkItem task, TaskInput input, CancellationToken ct)
    {
        task.Title = Input.Required(input.Title, "title", 160);
        task.Description = Input.Required(input.Description, "description", 10000);
        if (input.AcceptanceCriteria is null || input.AcceptanceCriteria.Length > 30)
            throw new ApiException(400, "invalid_criteria", "Provide at most 30 acceptance criteria.");
        task.AcceptanceCriteria = input.AcceptanceCriteria.Select(value => Input.Required(value, "criterion", 1000)).ToArray();
        if (input.AssigneeId is Guid assignee && !await organizations.IsMemberAsync(task.OrganizationId, assignee, ct))
            throw new ApiException(400, "invalid_assignee", "Assignee must belong to this organization.");
        await tasks.ValidateDependenciesAsync(task, input.DependencyIds, ct);
        task.AssigneeId = input.AssigneeId;
        task.DependencyIds = input.DependencyIds;
    }

    private static void ApplyKind(WorkItem task, TaskInput input)
    {
        if (input.Kind is not (null or "change" or "analysis"))
            throw new ApiException(400, "invalid_kind", "A task is either a change or an analysis.");
        if (input.Kind != "analysis") return;
        if (string.IsNullOrWhiteSpace(input.FolderPath))
            throw new ApiException(400, "folder_required", "An analysis needs the folder to read.");
        task.Kind = "analysis";
    }

    private static void ApplyInitialStage(WorkItem task, TaskStage? requested)
    {
        if (requested is not { } stage) return;
        if (stage is not (TaskStage.Intake or TaskStage.Ready))
            throw new ApiException(400, "invalid_stage", "A new task can start only in Intake or Ready.");
        if (stage == TaskStage.Ready && task.AcceptanceCriteria.Length == 0)
            throw new ApiException(409, "criteria_required", "Acceptance criteria are required before execution.");
        task.Stage = stage;
    }

    private async Task ApplyFolderAsync(WorkItem task, TaskInput input, CancellationToken ct)
    {
        if (input.FolderPath is not { } folderPath)
        {
            if (input.BaseBranch is not null) throw new ApiException(400, "invalid_repository", "A base branch needs a folder.");
            return;
        }
        var connection = await repositories.EnsureTaskFolderAsync(task.OrganizationId, task.ProjectId, folderPath, input.BaseBranch, ct);
        task.RepositoryConnectionId = connection.Id;
        task.FolderPath = connection.LocalPath;
        task.BaseBranch = connection.BaseBranch;
    }

    private async Task LinkPreviousAsync(WorkItem task, Guid previousId, CancellationToken ct)
    {
        var previous = await db.WorkItems.AsNoTracking().SingleOrDefaultAsync(t => t.Id == previousId && t.OrganizationId == task.OrganizationId && t.ProjectId == task.ProjectId, ct)
            ?? throw new ApiException(404, "task_not_found", "The previous step was not found.");
        if (task.Kind != "change" || previous.Kind != "change" || task.RepositoryConnectionId is null || previous.RepositoryConnectionId != task.RepositoryConnectionId)
            throw new ApiException(400, "invalid_sequence", "A sequence links change tasks that use the same folder and branch.");
        if (previous.Stage is TaskStage.Failed or TaskStage.Cancelled or TaskStage.Blocked)
            throw new ApiException(409, "invalid_sequence", "The previous step failed or was cancelled; continue from another task.");
        if (await db.WorkItems.AnyAsync(t => t.PreviousTaskId == previousId, ct))
            throw new ApiException(409, "sequence_branch", "That task already has a next step.");
        task.PreviousTaskId = previousId;
    }
}
