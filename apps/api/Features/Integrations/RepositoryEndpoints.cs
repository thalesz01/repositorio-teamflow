using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Projects;
using TeamFlow.Api.Features.Executions;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Integrations;

public sealed record RepositoryTokenInput(Guid LeaseToken);

public static class RepositoryEndpoints
{
    public static GitDto ToDto(GitConnection connection) => new(connection.Id, connection.Provider, connection.RepositoryId,
        GitRules.Url(connection), connection.BaseBranch, connection.BaseCommit, connection.Version, connection.RevokedAt);

    public static void MapRepositories(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/organizations/{organizationId:guid}/projects/{projectId:guid}/repository")
            .RequireAuthorization().WithTags("Repository connection");
        group.MapGet("/local/directories", async (Guid organizationId, Guid projectId, string? path, ClaimsPrincipal user,
            ProjectAccess access, IConfiguration configuration, CancellationToken ct) =>
        {
            await access.RequireAsync(organizationId, projectId, user, ct, owner: true);
            return Results.Ok(LocalDirectoryBrowser.Browse(configuration, organizationId, projectId, path));
        }).Produces<LocalDirectoryListing>();
        group.MapPost("/local/inspect", async (Guid organizationId, Guid projectId, LocalFolderInput input, ClaimsPrincipal user,
            ProjectAccess access, LocalRepository local, CancellationToken ct) =>
        {
            await access.RequireAsync(organizationId, projectId, user, ct, owner: true);
            return Results.Ok(await local.InspectAsync(organizationId, projectId, input.Path, ct));
        }).Produces<LocalFolderInspection>();
        group.MapPost("/local/folder", async (Guid organizationId, Guid projectId, LocalFolderInput input, ClaimsPrincipal user,
            ProjectAccess access, LocalRepository local, AppDbContext db, CancellationToken ct) =>
        {
            await access.RequireAsync(organizationId, projectId, user, ct, owner: true);
            var folder = await local.InspectAsync(organizationId, projectId, input.Path, ct);
            var branch = input.BaseBranch ?? "";
            if (branch.Length > 0 && (!folder.HasGit || !folder.Branches.Contains(branch)))
                throw new ApiException(400, "invalid_repository", "Select an existing local branch.");
            var commit = branch.Length > 0 ? await local.VerifyAsync(folder.Path, branch, ct) : "";
            var connection = await db.GitConnections.SingleOrDefaultAsync(g => g.OrganizationId == organizationId && g.ProjectId == projectId && g.RevokedAt == null, ct);
            if (connection is not null && (connection.Provider != "local" || input.Version != connection.Version))
                throw new ApiException(409, "version_conflict", "Folder configuration changed. Reload before saving.");
            if (connection is null)
            {
                connection = new() { Id = Guid.NewGuid(), OrganizationId = organizationId, ProjectId = projectId, Provider = "local" };
                db.GitConnections.Add(connection);
            }
            connection.LocalPath = folder.Path; connection.BaseBranch = branch; connection.BaseCommit = commit; connection.Version = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(connection));
        }).Produces<GitDto>();
        group.MapPost("/local", async (Guid organizationId, Guid projectId, LocalRepositoryInput input, ClaimsPrincipal user,
            ProjectAccess access, AppDbContext db, LocalRepository local, CancellationToken ct) =>
        {
            await access.RequireAsync(organizationId, projectId, user, ct, owner: true);
            var path = local.AuthorizedPath(organizationId, projectId, input.Path);
            if (await db.GitConnections.AnyAsync(g => g.OrganizationId == organizationId && g.ProjectId == projectId && g.RevokedAt == null, ct))
                throw new ApiException(409, "repository_connected", "Revoke the existing connection before replacing it.");
            var connection = new GitConnection
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                ProjectId = projectId,
                Provider = "local",
                LocalPath = path,
                BaseBranch = input.BaseBranch,
                BaseCommit = await local.VerifyAsync(path, input.BaseBranch, ct),
                Version = Guid.NewGuid()
            };
            db.GitConnections.Add(connection); await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(connection));
        }).Produces<GitDto>();
        group.MapGet("/", async (Guid organizationId, Guid projectId, ClaimsPrincipal user, ProjectAccess access, AppDbContext db, CancellationToken ct) =>
        {
            await access.RequireAsync(organizationId, projectId, user, ct);
            var connection = await db.GitConnections.AsNoTracking().SingleOrDefaultAsync(g => g.OrganizationId == organizationId
                && g.ProjectId == projectId && g.RevokedAt == null, ct);
            return connection is null ? Results.NotFound() : Results.Ok(ToDto(connection));
        }).Produces<GitDto>().Produces(404);
        group.MapGet("/local/reviews/{executionId:guid}", async (Guid organizationId, Guid projectId, Guid executionId,
            ClaimsPrincipal user, PublicationAccess publication, CancellationToken ct) =>
            Results.Text(await publication.LocalDiffAsync(organizationId, projectId, executionId, user, ct), "text/plain"));
        group.MapDelete("/", async (Guid organizationId, Guid projectId, ClaimsPrincipal user, ProjectAccess access,
            AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            await access.RequireAsync(organizationId, projectId, user, ct, owner: true);
            await db.GitConnections.Where(g => g.OrganizationId == organizationId && g.ProjectId == projectId && g.RevokedAt == null)
                .ExecuteUpdateAsync(set => set.SetProperty(g => g.RevokedAt, clock.GetUtcNow())
                    .SetProperty(g => g.Version, Guid.NewGuid()), ct);
            return Results.NoContent();
        });
        routes.MapPost("/internal/v1/executions/{id:guid}/repository-token", async (Guid id, RepositoryTokenInput input,
            ClaimsPrincipal user, RepositoryAccess access, CancellationToken ct) =>
            Results.Ok(await access.CheckoutGrantAsync(id, input.LeaseToken, user, ct)))
            .RequireAuthorization("runner").RequireRateLimiting("runner").Produces<CheckoutGrant>().WithTags("Runner protocol");
    }
}
