using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Executions;

public static class ExecutionEndpoints
{
    public static void MapExecutions(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/organizations/{organizationId:guid}/projects/{projectId:guid}/tasks/{taskId:guid}/executions")
            .WithTags("Executions").RequireAuthorization();
        group.MapPost("/", ScheduleAsync).Produces<ExecutionDto>();
        group.MapGet("/", ListAsync).Produces<PageResult<ExecutionDto>>();
        group.MapPost("/{executionId:guid}/cancel", CancelAsync).Produces<ExecutionDto>();
    }

    private static async Task<IResult> ScheduleAsync(Guid organizationId, Guid projectId, Guid taskId, ExecutionInput input,
        [FromHeader(Name = "Idempotency-Key")] string? key, ClaimsPrincipal user, ExecutionScheduler scheduler, CancellationToken ct) =>
        Results.Ok(await scheduler.ScheduleAsync(organizationId, projectId, taskId, input, key, user, ct));

    private static async Task<IResult> ListAsync(Guid organizationId, Guid projectId, Guid taskId, int? page, int? pageSize,
        ClaimsPrincipal user, ExecutionScheduler scheduler, CancellationToken ct) =>
        Results.Ok(await scheduler.ListAsync(organizationId, projectId, taskId, page, pageSize, user, ct));

    private static async Task<IResult> CancelAsync(Guid organizationId, Guid projectId, Guid taskId, Guid executionId,
        ClaimsPrincipal user, ExecutionScheduler scheduler, CancellationToken ct) =>
        Results.Ok(await scheduler.CancelAsync(organizationId, projectId, taskId, executionId, user, ct));
}
