using System.Security.Claims;
using TeamFlow.Api.Features.Executions;

namespace TeamFlow.Api.Features.Integrations;

public sealed record RepositoryTokenInput(Guid LeaseToken);

public static class RepositoryEndpoints
{
    public static void MapRepositories(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/organizations/{organizationId:guid}/projects/{projectId:guid}/repository")
            .RequireAuthorization().WithTags("Repository connection");
        group.MapGet("/local/directories", BrowseAsync).Produces<LocalDirectoryListing>();
        group.MapPost("/local/inspect", InspectAsync).Produces<LocalFolderInspection>();
        group.MapPost("/local/folder", SaveFolderAsync).Produces<GitDto>();
        group.MapPost("/local", ConnectAsync).Produces<GitDto>();
        group.MapGet("/", GetAsync).Produces<GitDto>().Produces(404);
        group.MapGet("/local/reviews/{executionId:guid}", ReviewAsync);
        group.MapDelete("/", RevokeAsync);
        routes.MapPost("/internal/v1/executions/{id:guid}/repository-token", CheckoutGrantAsync)
            .RequireAuthorization("runner").RequireRateLimiting("runner").Produces<CheckoutGrant>().WithTags("Runner protocol");
    }

    private static async Task<IResult> BrowseAsync(Guid organizationId, Guid projectId, string? path, ClaimsPrincipal user, RepositoryService service, CancellationToken ct) =>
        Results.Ok(await service.BrowseAsync(organizationId, projectId, path, user, ct));

    private static async Task<IResult> InspectAsync(Guid organizationId, Guid projectId, LocalFolderInput input, ClaimsPrincipal user, RepositoryService service, CancellationToken ct) =>
        Results.Ok(await service.InspectAsync(organizationId, projectId, input, user, ct));

    private static async Task<IResult> SaveFolderAsync(Guid organizationId, Guid projectId, LocalFolderInput input, ClaimsPrincipal user, RepositoryService service, CancellationToken ct) =>
        Results.Ok(GitDto.From(await service.SaveFolderAsync(organizationId, projectId, input, user, ct)));

    private static async Task<IResult> ConnectAsync(Guid organizationId, Guid projectId, LocalRepositoryInput input, ClaimsPrincipal user, RepositoryService service, CancellationToken ct) =>
        Results.Ok(GitDto.From(await service.ConnectAsync(organizationId, projectId, input, user, ct)));

    private static async Task<IResult> GetAsync(Guid organizationId, Guid projectId, ClaimsPrincipal user, RepositoryService service, CancellationToken ct) =>
        await service.GetAsync(organizationId, projectId, user, ct) is { } connection ? Results.Ok(GitDto.From(connection)) : Results.NotFound();

    private static async Task<IResult> ReviewAsync(Guid organizationId, Guid projectId, Guid executionId, ClaimsPrincipal user, PublicationAccess publication, CancellationToken ct) =>
        Results.Text(await publication.LocalDiffAsync(organizationId, projectId, executionId, user, ct), "text/plain");

    private static async Task<IResult> RevokeAsync(Guid organizationId, Guid projectId, ClaimsPrincipal user, RepositoryService service, CancellationToken ct)
    {
        await service.RevokeAsync(organizationId, projectId, user, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> CheckoutGrantAsync(Guid id, RepositoryTokenInput input, ClaimsPrincipal user, RepositoryAccess access, CancellationToken ct) =>
        Results.Ok(await access.CheckoutGrantAsync(id, input.LeaseToken, user, ct));
}
