using System.Security.Claims;
using TeamFlow.Api.Features.Executions;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Tasks;

public static class TaskEndpoints
{
    public static void MapTasks(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/organizations/{organizationId:guid}/projects/{projectId:guid}/tasks").WithTags("Tasks").RequireAuthorization();
        group.MapGet("/", ListAsync).Produces<PageResult<TaskDto>>();
        group.MapPost("/", CreateAsync).Produces<TaskDto>(201);
        group.MapGet("/{taskId:guid}", GetAsync);
        group.MapPut("/{taskId:guid}", UpdateAsync).Produces<TaskDto>();
        group.MapPost("/{taskId:guid}/transition", TransitionAsync).Produces<TaskDto>();
        group.MapPost("/{taskId:guid}/sequence/start", StartSequenceAsync).Produces<TaskDto[]>();
        group.MapPost("/{taskId:guid}/sequence/stop", StopSequenceAsync).Produces<TaskDto[]>();
        group.MapGet("/{taskId:guid}/skills", ListSkillsAsync).Produces<SkillInfo[]>();
    }

    private static async Task<IResult> ListAsync(Guid organizationId, Guid projectId, int? page, int? pageSize,
        ClaimsPrincipal user, TaskService service, CancellationToken ct) =>
        Results.Ok(await service.ListAsync(organizationId, projectId, page, pageSize, user, ct));

    private static async Task<IResult> GetAsync(Guid organizationId, Guid projectId, Guid taskId,
        ClaimsPrincipal user, TaskAccess access, CancellationToken ct) =>
        Results.Ok(TaskDto.From(await access.RequireAsync(organizationId, projectId, taskId, user, ct)));

    private static async Task<IResult> CreateAsync(Guid organizationId, Guid projectId, TaskInput request,
        ClaimsPrincipal user, TaskService service, CancellationToken ct)
    {
        var task = await service.CreateAsync(organizationId, projectId, request, user, ct);
        return Results.Created($"/api/v1/organizations/{organizationId}/projects/{projectId}/tasks/{task.Id}", TaskDto.From(task));
    }

    private static async Task<IResult> UpdateAsync(Guid organizationId, Guid projectId, Guid taskId, TaskUpdate request,
        ClaimsPrincipal user, TaskService service, CancellationToken ct) =>
        Results.Ok(TaskDto.From(await service.UpdateAsync(organizationId, projectId, taskId, request, user, ct)));

    private static async Task<IResult> TransitionAsync(Guid organizationId, Guid projectId, Guid taskId, TransitionInput request,
        ClaimsPrincipal user, TaskService service, CancellationToken ct) =>
        Results.Ok(TaskDto.From(await service.TransitionAsync(organizationId, projectId, taskId, request, user, ct)));

    private static async Task<IResult> StartSequenceAsync(Guid organizationId, Guid projectId, Guid taskId,
        ClaimsPrincipal user, TaskAccess access, TaskSequences sequences, CancellationToken ct)
    {
        await access.RequireAsync(organizationId, projectId, taskId, user, ct);
        return Results.Ok((await sequences.StartAsync(organizationId, projectId, taskId, user, ct)).Select(TaskDto.From).ToArray());
    }

    private static async Task<IResult> StopSequenceAsync(Guid organizationId, Guid projectId, Guid taskId,
        ClaimsPrincipal user, TaskAccess access, TaskSequences sequences, CancellationToken ct)
    {
        await access.RequireAsync(organizationId, projectId, taskId, user, ct);
        return Results.Ok((await sequences.StopAsync(organizationId, projectId, taskId, ct)).Select(TaskDto.From).ToArray());
    }

    private static async Task<IResult> ListSkillsAsync(Guid organizationId, Guid projectId, Guid taskId,
        ClaimsPrincipal user, TaskAccess access, LocalSkills skills, CancellationToken ct)
    {
        var task = await access.RequireAsync(organizationId, projectId, taskId, user, ct);
        return Results.Ok(skills.List(organizationId, projectId, task.FolderPath));
    }
}
