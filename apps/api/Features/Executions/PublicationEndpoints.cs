using System.Security.Claims;

namespace TeamFlow.Api.Features.Executions;

public static class PublicationEndpoints
{
    public static void MapPublication(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/organizations/{organizationId:guid}/projects/{projectId:guid}/tasks/{taskId:guid}/executions/{executionId:guid}")
            .WithTags("Executions").RequireAuthorization();
        group.MapGet("/publication", ReadAsync).Produces<PublicationStatus>();
        group.MapPost("/publication", PublishAsync).Produces<PublicationStatus>();
    }

    private static async Task<PublicationStatus> ReadAsync(Guid organizationId, Guid projectId, Guid taskId, Guid executionId,
        ClaimsPrincipal user, PublicationAccess publication, CancellationToken ct) =>
        await publication.ReadAsync(organizationId, projectId, taskId, executionId, user, ct);

    private static async Task<PublicationStatus> PublishAsync(Guid organizationId, Guid projectId, Guid taskId, Guid executionId,
        ClaimsPrincipal user, PublicationAccess publication, CancellationToken ct)
    {
        await publication.ReadAsync(organizationId, projectId, taskId, executionId, user, ct);
        return await publication.PublishAsync(executionId, ct);
    }
}
