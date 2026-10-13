using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Executions;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Integrations;

public sealed class RepositoryAccess(AppDbContext db, IntegrationSecrets secrets, IGitProvider git, LeaseAccess leases, LocalRepository local, TimeProvider clock, LocalGit localGit)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string?> SnapshotAsync(Guid organizationId, Guid projectId, Guid executionId, CancellationToken ct)
    {
        var connection = await db.GitConnections.SingleOrDefaultAsync(g => g.OrganizationId == organizationId
            && g.ProjectId == projectId && g.RevokedAt == null, ct);
        if (connection is { Provider: "local" } && (!GitRules.IsCommit(connection.BaseCommit) || string.IsNullOrWhiteSpace(connection.BaseBranch)
            || !Directory.Exists(Path.Combine(connection.LocalPath, ".git"))))
        {
            var lockPath = OperatingSystem.IsWindows() ? connection.LocalPath.ToUpperInvariant() : connection.LocalPath;
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({"local-base:" + lockPath}))", ct);
            await db.Entry(connection).ReloadAsync(ct);
            if (connection.RevokedAt is not null) throw new ApiException(409, "repository_revoked", "Folder configuration was revoked.");
            if (!GitRules.IsCommit(connection.BaseCommit) || string.IsNullOrWhiteSpace(connection.BaseBranch))
            {
                await localGit.PrepareBaseAsync(connection, ct);
                connection.Version = Guid.NewGuid();
                await db.SaveChangesAsync(ct);
            }
        }
        return connection is null ? null : JsonSerializer.Serialize(new RepositorySnapshot(connection.Id, connection.Version,
            connection.RepositoryId, GitRules.Url(connection), connection.BaseBranch, connection.BaseCommit, $"teamflow/{executionId:N}", connection.Provider == "local" ? connection.LocalPath : null), JsonOptions);
    }

    public async Task<PublishedPullRequest> PublishAsync(Guid organizationId, Guid projectId, RepositorySnapshot snapshot,
        PublicationPlan plan, IGitPublisher publisher, CancellationToken ct)
    {
        var connection = await db.GitConnections.AsNoTracking().SingleOrDefaultAsync(g => g.Id == snapshot.ConnectionId
            && g.OrganizationId == organizationId && g.ProjectId == projectId && g.Version == snapshot.ConnectionVersion
            && g.RepositoryId == snapshot.RepositoryId && g.RevokedAt == null, ct)
            ?? throw new ApiException(409, "repository_revoked", "Repository connection was revoked or replaced.");
        if (connection.Provider == "local")
        {
            var published = await localGit.PublishAsync(connection, plan, ct);
            if (!await db.GitConnections.AnyAsync(g => g.Id == connection.Id && g.Version == snapshot.ConnectionVersion && g.RevokedAt == null, ct))
                throw new ApiException(409, "repository_revoked", "Local connection changed during publication; reconcile its branch.");
            return published;
        }
        var key = secrets.Unprotect(organizationId, projectId, connection.Id, "github", connection.ProtectedPrivateKey);
        var receipt = await publisher.PublishAsync(connection, key, plan, ct);
        if (!await db.GitConnections.AnyAsync(g => g.Id == connection.Id && g.Version == snapshot.ConnectionVersion && g.RevokedAt == null, ct))
            throw new ApiException(409, "repository_revoked", "Connection changed during publication; reconcile the external PR.");
        return receipt;
    }

    public async Task<GitReviewSnapshot> ReviewAsync(Guid organizationId, Guid projectId, RepositorySnapshot snapshot,
        long number, CiPolicy policy, IGitReview review, CancellationToken ct, string? expectedHead = null)
    {
        var connection = await db.GitConnections.AsNoTracking().SingleOrDefaultAsync(g => g.Id == snapshot.ConnectionId
            && g.OrganizationId == organizationId && g.ProjectId == projectId && g.Version == snapshot.ConnectionVersion
            && g.RepositoryId == snapshot.RepositoryId && g.RevokedAt == null, ct)
            ?? throw new ApiException(409, "repository_revoked", "Repository connection was revoked or replaced.");
        if (connection.Provider == "local")
        {
            var localResult = await localGit.ReviewAsync(connection, snapshot, expectedHead ?? "", ct);
            if (!await db.GitConnections.AnyAsync(g => g.Id == connection.Id && g.Version == snapshot.ConnectionVersion && g.RevokedAt == null, ct))
                throw new ApiException(409, "repository_revoked", "Local connection changed during review.");
            return localResult;
        }
        var key = secrets.Unprotect(organizationId, projectId, connection.Id, "github", connection.ProtectedPrivateKey);
        var result = await review.ReadAsync(connection, key, number, snapshot.Branch, policy, ct);
        if (!await db.GitConnections.AnyAsync(g => g.Id == connection.Id && g.Version == snapshot.ConnectionVersion && g.RevokedAt == null, ct))
            throw new ApiException(409, "repository_revoked", "Connection changed during review.");
        return result;
    }

    public async Task<string> LocalDiffAsync(Guid organizationId, Guid projectId, RepositorySnapshot snapshot, string head, CancellationToken ct)
    {
        var connection = await db.GitConnections.AsNoTracking().SingleOrDefaultAsync(g => g.Id == snapshot.ConnectionId
            && g.OrganizationId == organizationId && g.ProjectId == projectId && g.Version == snapshot.ConnectionVersion
            && g.Provider == "local" && g.RevokedAt == null, ct)
            ?? throw new ApiException(404, "repository_not_configured", "Active local connection not found.");
        var diff = await localGit.DiffAsync(connection, snapshot.BaseCommit, head, ct);
        if (!await db.GitConnections.AnyAsync(g => g.Id == connection.Id && g.Version == snapshot.ConnectionVersion && g.RevokedAt == null, ct))
            throw new ApiException(409, "repository_revoked", "Local connection changed during diff retrieval.");
        return diff;
    }

    public async Task<InstallationToken> CheckoutTokenAsync(Guid executionId, Guid leaseToken, ClaimsPrincipal user, CancellationToken ct)
    {
        var execution = await leases.RequireAsync(executionId, leaseToken, user, ct);
        var snapshot = execution.RepositorySnapshot is null ? null : JsonSerializer.Deserialize<RepositorySnapshot>(execution.RepositorySnapshot, JsonOptions);
        if (snapshot is null) throw new ApiException(409, "repository_not_configured", "Execution has no repository snapshot.");
        var connection = await db.GitConnections.AsNoTracking().SingleOrDefaultAsync(g => g.Id == snapshot.ConnectionId
            && g.OrganizationId == execution.OrganizationId && g.ProjectId == execution.ProjectId
            && g.Version == snapshot.ConnectionVersion && g.RevokedAt == null, ct)
            ?? throw new ApiException(409, "repository_revoked", "Repository connection was revoked or replaced.");
        if (connection.Provider == "local")
        {
            local.AuthorizedPath(connection.OrganizationId, connection.ProjectId, connection.LocalPath);
            if (await local.VerifyAsync(connection.LocalPath, connection.BaseBranch, ct) != snapshot.BaseCommit)
                throw new ApiException(409, "base_changed", "Local base branch changed; schedule a new validated execution.");
            await leases.RequireAsync(executionId, leaseToken, user, ct);
            if (!await db.GitConnections.AnyAsync(g => g.Id == connection.Id && g.Version == snapshot.ConnectionVersion && g.RevokedAt == null, ct))
                throw new ApiException(409, "repository_revoked", "Local connection changed during checkout authorization.");
            return new("local", clock.GetUtcNow().AddSeconds(60));
        }
        var key = secrets.Unprotect(connection.OrganizationId, connection.ProjectId, connection.Id, "github", connection.ProtectedPrivateKey);
        var token = await git.TokenAsync(connection, key, ct);
        await leases.RequireAsync(executionId, leaseToken, user, ct);
        // Revocation during token generation must also prevent credential delivery.
        if (!await db.GitConnections.AnyAsync(g => g.Id == connection.Id && g.Version == snapshot.ConnectionVersion && g.RevokedAt == null, ct))
            throw new ApiException(409, "repository_revoked", "Repository connection was revoked or replaced.");
        return token;
    }
}
