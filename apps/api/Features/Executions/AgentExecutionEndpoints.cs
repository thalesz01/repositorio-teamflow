using System.Security.Claims;

namespace TeamFlow.Api.Features.Executions;

public static class AgentExecutionEndpoints
{
    public static void MapAgentExecutions(this WebApplication app)
    {
        var group = app.MapGroup("/internal/v1/executions/{id:guid}")
            .RequireAuthorization("runner").RequireRateLimiting("runner").WithTags("Runner protocol");
        group.MapPost("/recovery", async (Guid id, RecoveryInput input, ClaimsPrincipal user, RecoveryAccess recovery, CancellationToken ct)
            => Results.Ok(await recovery.ReadAsync(id, input.LeaseToken, user, ct))).Produces<RecoveryReply>();
        group.MapPost("/model-turn", async (Guid id, ModelTurnInput input, ClaimsPrincipal user, ModelTurnAccess turns, CancellationToken ct)
            => Results.Ok(await turns.GenerateAsync(id, input, user, ct))).Produces<ModelTurnReply>();
        group.MapPost("/result", async (Guid id, ExecutionResultInput input, ClaimsPrincipal user, ExecutionResultAccess results, CancellationToken ct)
            => Results.Ok(await results.SaveAsync(id, input, user, ct))).Produces<ExecutionResultReply>();
    }
}
