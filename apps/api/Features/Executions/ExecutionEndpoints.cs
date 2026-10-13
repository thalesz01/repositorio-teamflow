using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Tasks;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Executions;

public static class ExecutionEndpoints
{
    public static void MapExecutions(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/organizations/{organizationId:guid}/projects/{projectId:guid}/tasks/{taskId:guid}/executions")
            .WithTags("Executions").RequireAuthorization();
        group.MapPost("/", async (Guid organizationId, Guid projectId, Guid taskId, ExecutionInput input,
            [FromHeader(Name = "Idempotency-Key")] string? key, ClaimsPrincipal user, ExecutionScheduler scheduler, CancellationToken ct) =>
            Results.Ok(await scheduler.ScheduleAsync(organizationId, projectId, taskId, input, key, user, ct))).Produces<ExecutionDto>();
        group.MapGet("/", ListAsync).Produces<PageResult<ExecutionDto>>();
        group.MapPost("/{executionId:guid}/cancel", async (Guid organizationId, Guid projectId, Guid taskId, Guid executionId,
            ClaimsPrincipal user, ExecutionScheduler scheduler, CancellationToken ct) =>
            Results.Ok(await scheduler.CancelAsync(organizationId, projectId, taskId, executionId, user, ct))).Produces<ExecutionDto>();
    }

    private static async Task<IResult> ListAsync(Guid organizationId, Guid projectId, Guid taskId, int? page, int? pageSize,
        ClaimsPrincipal user, TaskAccess tasks, AppDbContext db, CancellationToken ct)
    {
        await tasks.RequireAsync(organizationId, projectId, taskId, user, ct);
        var (number, size) = Pagination.Validate(page, pageSize);
        var query = db.Executions.AsNoTracking().Where(e => e.OrganizationId == organizationId && e.ProjectId == projectId && e.TaskId == taskId)
            .OrderByDescending(e => e.CreatedAt).ThenBy(e => e.Id);
        var executions = await query.Skip((number - 1) * size).Take(size).ToListAsync(ct);
        return Results.Ok(new PageResult<ExecutionDto>(executions.Select(ExecutionScheduler.ToDto).ToList(), number, size, await query.CountAsync(ct)));
    }
}
