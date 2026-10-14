using System.Security.Claims;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Runners;

public static class RunnerEndpoints
{
    public static void MapRunners(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/organizations/{organizationId:guid}/projects/{projectId:guid}/runners")
            .RequireAuthorization().WithTags("Runners");
        group.MapPost("/", IssueAsync).Produces<IssuedRunner>();
        group.MapGet("/status", StatusAsync).Produces<RunnerStatusDto>();
        group.MapGet("/", ListAsync).Produces<PageResult<RunnerDto>>();
        group.MapPost("/{id:guid}/rotate", RotateAsync).Produces<IssuedRunner>();
        group.MapDelete("/{id:guid}", RevokeAsync);

        var internalGroup = app.MapGroup("/internal/v1/executions").RequireAuthorization("runner").RequireRateLimiting("runner").WithTags("Runner protocol");
        internalGroup.MapPost("/claim", ClaimAsync).Produces<RunnerMessage>().Produces(204);
        internalGroup.MapPost("/{id:guid}/heartbeat", HeartbeatAsync).Produces<HeartbeatReply>().Produces<HeartbeatReply>(409);
    }

    private static async Task<IResult> IssueAsync(Guid organizationId, Guid projectId, RunnerInput input, ClaimsPrincipal user, RunnerService service, CancellationToken ct) =>
        Results.Ok(await service.IssueAsync(organizationId, projectId, input, user, ct));

    private static async Task<IResult> StatusAsync(Guid organizationId, Guid projectId, ClaimsPrincipal user, RunnerService service, CancellationToken ct) =>
        Results.Ok(await service.StatusAsync(organizationId, projectId, user, ct));

    private static async Task<IResult> ListAsync(Guid organizationId, Guid projectId, ClaimsPrincipal user, RunnerService service, CancellationToken ct,
        int page = 1, int pageSize = 20) =>
        Results.Ok(await service.ListAsync(organizationId, projectId, page, pageSize, user, ct));

    private static async Task<IResult> RotateAsync(Guid organizationId, Guid projectId, Guid id, RunnerInput input, ClaimsPrincipal user, RunnerService service, CancellationToken ct) =>
        Results.Ok(await service.RotateAsync(organizationId, projectId, id, input, user, ct));

    private static async Task<IResult> RevokeAsync(Guid organizationId, Guid projectId, Guid id, ClaimsPrincipal user, RunnerService service, CancellationToken ct)
    {
        await service.RevokeAsync(organizationId, projectId, id, user, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ClaimAsync(ClaimInput input, ClaimsPrincipal runner, RunnerService service, CancellationToken ct) =>
        await service.ClaimAsync(input, runner, ct) is { } message ? Results.Ok(message) : Results.NoContent();

    private static async Task<IResult> HeartbeatAsync(Guid id, HeartbeatInput input, ClaimsPrincipal runner, RunnerService service, CancellationToken ct)
    {
        var reply = await service.HeartbeatAsync(id, input, runner, ct);
        return reply.ContinueExecution ? Results.Ok(reply) : Results.Conflict(reply);
    }
}
