using System.Security.Claims;

namespace TeamFlow.Api.Features.Workspace;

public static class WorkspaceEndpoints
{
    public static void MapWorkspace(this IEndpointRouteBuilder routes) =>
        routes.MapPost("/api/v1/workspace", ResolveAsync).RequireAuthorization().WithTags("Workspace").Produces<WorkspaceDto>();

    private static async Task<IResult> ResolveAsync(ClaimsPrincipal user, WorkspaceService service, CancellationToken ct) =>
        Results.Ok(await service.ResolveAsync(user, ct));
}
