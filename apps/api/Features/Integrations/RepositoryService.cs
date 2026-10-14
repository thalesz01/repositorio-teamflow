using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Projects;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Integrations;

public sealed class RepositoryService(AppDbContext db, ProjectAccess projects, LocalRepository local, IConfiguration configuration, TimeProvider clock)
{
    public async Task<LocalDirectoryListing> BrowseAsync(Guid organizationId, Guid projectId, string? path, ClaimsPrincipal user, CancellationToken ct)
    {
        await projects.RequireAsync(organizationId, projectId, user, ct, owner: true);
        return LocalDirectoryBrowser.Browse(configuration, organizationId, projectId, path);
    }

    public async Task<LocalFolderInspection> InspectAsync(Guid organizationId, Guid projectId, LocalFolderInput input, ClaimsPrincipal user, CancellationToken ct)
    {
        await projects.RequireAsync(organizationId, projectId, user, ct, owner: true);
        return await local.InspectAsync(organizationId, projectId, input.Path, ct);
    }

    public async Task<GitConnection> SaveFolderAsync(Guid organizationId, Guid projectId, LocalFolderInput input, ClaimsPrincipal user, CancellationToken ct)
    {
        await projects.RequireAsync(organizationId, projectId, user, ct, owner: true);
        var folder = await local.InspectAsync(organizationId, projectId, input.Path, ct);
        var branch = input.BaseBranch ?? "";
        if (branch.Length > 0 && (!folder.HasGit || !folder.Branches.Contains(branch)))
            throw new ApiException(400, "invalid_repository", "Select an existing local branch.");
        var commit = branch.Length > 0 ? await local.VerifyAsync(folder.Path, branch, ct) : "";
        var connection = await FindActiveAsync(organizationId, projectId, track: true, ct);
        if (connection is not null && (connection.Provider != "local" || input.Version != connection.Version))
            throw new ApiException(409, "version_conflict", "Folder configuration changed. Reload before saving.");
        if (connection is null)
        {
            connection = new() { Id = Guid.NewGuid(), OrganizationId = organizationId, ProjectId = projectId, Provider = "local" };
            db.GitConnections.Add(connection);
        }
        connection.LocalPath = folder.Path;
        connection.BaseBranch = branch;
        connection.BaseCommit = commit;
        connection.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return connection;
    }

    public async Task<GitConnection> ConnectAsync(Guid organizationId, Guid projectId, LocalRepositoryInput input, ClaimsPrincipal user, CancellationToken ct)
    {
        await projects.RequireAsync(organizationId, projectId, user, ct, owner: true);
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
        db.GitConnections.Add(connection);
        await db.SaveChangesAsync(ct);
        return connection;
    }

    public async Task<GitConnection?> GetAsync(Guid organizationId, Guid projectId, ClaimsPrincipal user, CancellationToken ct)
    {
        await projects.RequireAsync(organizationId, projectId, user, ct);
        return await FindActiveAsync(organizationId, projectId, track: false, ct);
    }

    public async Task RevokeAsync(Guid organizationId, Guid projectId, ClaimsPrincipal user, CancellationToken ct)
    {
        await projects.RequireAsync(organizationId, projectId, user, ct, owner: true);
        await db.GitConnections.Where(g => g.OrganizationId == organizationId && g.ProjectId == projectId && g.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(g => g.RevokedAt, clock.GetUtcNow())
                .SetProperty(g => g.Version, Guid.NewGuid()), ct);
    }

    private Task<GitConnection?> FindActiveAsync(Guid organizationId, Guid projectId, bool track, CancellationToken ct)
    {
        var connections = track ? db.GitConnections : db.GitConnections.AsNoTracking();
        return connections.SingleOrDefaultAsync(g => g.OrganizationId == organizationId && g.ProjectId == projectId && g.RevokedAt == null, ct);
    }
}
