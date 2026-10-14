using System.Security.Claims;
using TeamFlow.Api.Features.Auth;

namespace TeamFlow.Api.Features.Executions;

public static class ReviewEndpoints
{
    public static void MapReviews(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/organizations/{organizationId:guid}/projects/{projectId:guid}/tasks/{taskId:guid}/executions/{executionId:guid}")
            .RequireAuthorization().WithTags("Reviews");
        group.MapGet("/review", ReadAsync);
        group.MapPost("/review/approve", ApproveAsync);
        group.MapPost("/review/refresh", RefreshAsync);
        group.MapPost("/review/merge", MergeAsync);
        group.MapPost("/review/complete", CompleteAsync);
    }

    private static async Task<ReviewStatus> ReadAsync(Guid organizationId, Guid projectId, Guid taskId, Guid executionId,
        ClaimsPrincipal user, ReviewAccess reviews, CancellationToken ct) =>
        await reviews.ReadAsync(organizationId, projectId, taskId, executionId, user, ct);

    private static async Task<ReviewStatus> ApproveAsync(Guid organizationId, Guid projectId, Guid taskId, Guid executionId,
        ClaimsPrincipal user, ReviewAccess reviews, CancellationToken ct)
    {
        await reviews.ReadAsync(organizationId, projectId, taskId, executionId, user, ct);
        return await reviews.ReconcileAsync(executionId, AuthEndpoints.UserId(user), ct);
    }

    private static async Task<ReviewStatus> RefreshAsync(Guid organizationId, Guid projectId, Guid taskId, Guid executionId,
        ClaimsPrincipal user, ReviewAccess reviews, CancellationToken ct)
    {
        await reviews.ReadAsync(organizationId, projectId, taskId, executionId, user, ct);
        return await reviews.ReconcileAsync(executionId, null, ct);
    }

    private static async Task<ReviewStatus> MergeAsync(Guid organizationId, Guid projectId, Guid taskId, Guid executionId,
        ClaimsPrincipal user, ReviewAccess reviews, CancellationToken ct)
    {
        await reviews.ReadAsync(organizationId, projectId, taskId, executionId, user, ct);
        return await reviews.MergeAsync(executionId, AuthEndpoints.UserId(user), ct);
    }

    private static async Task<ReviewStatus> CompleteAsync(Guid organizationId, Guid projectId, Guid taskId, Guid executionId,
        ClaimsPrincipal user, ReviewAccess reviews, CancellationToken ct)
    {
        await reviews.ReadAsync(organizationId, projectId, taskId, executionId, user, ct);
        return await reviews.CompleteAsync(executionId, AuthEndpoints.UserId(user), ct);
    }
}
