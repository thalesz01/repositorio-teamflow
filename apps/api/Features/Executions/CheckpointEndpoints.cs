using System.Security.Claims;

namespace TeamFlow.Api.Features.Executions;

public static class CheckpointEndpoints
{
    public static void MapCheckpoints(this WebApplication app)
    {
        var group = app.MapGroup("/internal/v1/executions/{id:guid}/checkpoint")
            .RequireAuthorization("runner").RequireRateLimiting("runner").WithTags("Runner protocol");
        group.MapPut("/", async (Guid id, CheckpointInput input, ClaimsPrincipal user, CheckpointAccess checkpoints, CancellationToken ct)
            => Results.Ok(await checkpoints.SaveAsync(id, input, user, ct))).Produces<CheckpointReply>();
        group.MapPost("/read", async (Guid id, CheckpointReadInput input, ClaimsPrincipal user, CheckpointAccess checkpoints, CancellationToken ct)
            => await checkpoints.ReadAsync(id, input.LeaseToken, user, ct) is { } saved
                ? Results.Ok(saved) : Results.NoContent()).Produces<CheckpointReply>().Produces(204);
    }
}
