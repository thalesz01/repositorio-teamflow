using TeamFlow.Api.Features.Memory;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Organizations;
using TeamFlow.Api.Features.Projects;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Tasks;

public sealed record TaskInput(string Title, string Description, string[] AcceptanceCriteria, Guid? AssigneeId, Guid[] DependencyIds, Guid? AnalysisId = null);
public sealed record TaskUpdate(string Title, string Description, string[] AcceptanceCriteria, Guid? AssigneeId, Guid[] DependencyIds, Guid Version);
public sealed record TransitionInput(TaskStage Stage, Guid Version);
public sealed record TaskDto(Guid Id, Guid OrganizationId, Guid ProjectId, string Title, string Description,
    string[] AcceptanceCriteria, Guid? AssigneeId, Guid[] DependencyIds, TaskStage Stage, Guid Version, DateTimeOffset CreatedAt, TaskStage[] AllowedTransitions, Guid? AnalysisId = null);

public static class TaskEndpoints
{
    public static void MapTasks(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/organizations/{organizationId:guid}/projects/{projectId:guid}/tasks").WithTags("Tasks").RequireAuthorization();
        group.MapGet("/", ListAsync).Produces<PageResult<TaskDto>>();
        group.MapPost("/", CreateAsync).Produces<TaskDto>(201);
        group.MapGet("/{taskId:guid}", async (Guid organizationId, Guid projectId, Guid taskId, ClaimsPrincipal user, TaskAccess access, CancellationToken ct) =>
            ToDto(await access.RequireAsync(organizationId, projectId, taskId, user, ct)));
        group.MapPut("/{taskId:guid}", UpdateAsync).Produces<TaskDto>();
        group.MapPost("/{taskId:guid}/transition", TransitionAsync).Produces<TaskDto>();
    }

    public static TaskDto ToDto(WorkItem task) => new(task.Id, task.OrganizationId, task.ProjectId, task.Title, task.Description,
        task.AcceptanceCriteria, task.AssigneeId, task.DependencyIds, task.Stage, task.Version, task.CreatedAt, TaskRules.AllowedTransitions(task.Stage), task.AnalysisId);

    private static async Task<IResult> ListAsync(Guid organizationId, Guid projectId, int? page, int? pageSize,
        ClaimsPrincipal user, ProjectAccess access, AppDbContext db, CancellationToken ct)
    {
        await access.RequireAsync(organizationId, projectId, user, ct);
        var (number, size) = Pagination.Validate(page, pageSize);
        var query = db.WorkItems.AsNoTracking().Where(t => t.OrganizationId == organizationId && t.ProjectId == projectId).OrderBy(t => t.CreatedAt).ThenBy(t => t.Id);
        var tasks = await query.Skip((number - 1) * size).Take(size).ToListAsync(ct);
        return Results.Ok(new PageResult<TaskDto>(tasks.Select(ToDto).ToList(), number, size, await query.CountAsync(ct)));
    }

    private static async Task<IResult> CreateAsync(Guid organizationId, Guid projectId, TaskInput request, ClaimsPrincipal user,
        ProjectAccess projects, OrganizationAccess organizations, TaskAccess tasks, PlanningAccess planning, AppDbContext db, CancellationToken ct)
    {
        await projects.RequireAsync(organizationId, projectId, user, ct);
        var task = new WorkItem { Id = Guid.NewGuid(), OrganizationId = organizationId, ProjectId = projectId };
        await ApplyInputAsync(task, request, organizations, tasks, ct);
        if (request.AnalysisId is { } analysisId)
        {
            await planning.ContextAsync(organizationId, projectId, analysisId, user, ct);
            task.AnalysisId = analysisId;
        }
        db.WorkItems.Add(task);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/organizations/{organizationId}/projects/{projectId}/tasks/{task.Id}", ToDto(task));
    }

    private static async Task<IResult> UpdateAsync(Guid organizationId, Guid projectId, Guid taskId, TaskUpdate request,
        ClaimsPrincipal user, TaskAccess tasks, OrganizationAccess organizations, AppDbContext db, CancellationToken ct)
    {
        var task = await tasks.RequireAsync(organizationId, projectId, taskId, user, ct);
        TaskRules.RequireVersion(task, request.Version);
        if (task.Stage is TaskStage.Execution or TaskStage.Review or TaskStage.AwaitingMerge or TaskStage.Done or TaskStage.Cancelled)
            throw new ApiException(409, "task_not_editable", "Task cannot be edited in its current stage.");
        // Serialize graph edits per project so concurrent requests cannot introduce a dependency cycle.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({projectId.ToString()}))", ct);
        await ApplyInputAsync(task, new TaskInput(request.Title, request.Description, request.AcceptanceCriteria, request.AssigneeId, request.DependencyIds), organizations, tasks, ct);
        task.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(ToDto(task));
    }

    private static async Task ApplyInputAsync(WorkItem task, TaskInput input, OrganizationAccess organizations, TaskAccess tasks, CancellationToken ct)
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

    private static async Task<IResult> TransitionAsync(Guid organizationId, Guid projectId, Guid taskId, TransitionInput request,
        ClaimsPrincipal user, TaskAccess tasks, AppDbContext db, CancellationToken ct)
    {
        var task = await tasks.RequireAsync(organizationId, projectId, taskId, user, ct);
        TaskRules.RequireVersion(task, request.Version);
        TaskRules.RequireTransition(task, request.Stage);
        task.Stage = request.Stage;
        task.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(task));
    }
}
