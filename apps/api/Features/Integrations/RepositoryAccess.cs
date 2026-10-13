using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Executions;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Integrations;

public sealed class RepositoryAccess(AppDbContext db, LeaseAccess leases, LocalRepository local, TimeProvider clock, LocalGit localGit,
    IGitPublisher publisher, IGitReview review, IGitMerge merge)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Finds or creates the connection for a folder chosen while creating a task. The folder is not modified.</summary>
    public async Task<GitConnection> EnsureTaskFolderAsync(Guid organizationId, Guid projectId, string path, string? branch, CancellationToken ct)
    {
        var folder = await local.InspectAsync(organizationId, projectId, path, ct);
        var name = branch ?? "";
        if (name.Length > 0 && (!folder.HasGit || !folder.Branches.Contains(name)))
            throw new ApiException(400, "invalid_repository", "Select an existing local branch.");
        if (name.Length == 0 && folder.HasGit && folder.Branches.Length > 0)
            name = folder.CurrentBranch is { } current && folder.Branches.Contains(current) ? current : folder.Branches[0];
        Task<GitConnection?> Find() => db.GitConnections.SingleOrDefaultAsync(g => g.OrganizationId == organizationId && g.ProjectId == projectId
            && g.ForTasks && g.RevokedAt == null && g.LocalPath == folder.Path && g.BaseBranch == name, ct);
        if (await Find() is { } existing) return existing;
        var connection = new GitConnection
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ProjectId = projectId,
            Provider = "local",
            ForTasks = true,
            LocalPath = folder.Path,
            BaseBranch = name,
            BaseCommit = name.Length > 0 ? await local.VerifyAsync(folder.Path, name, ct) : "",
            Version = Guid.NewGuid()
        };
        db.GitConnections.Add(connection);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException failure) when (failure.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // Another request created the same folder first; use its connection.
            db.Entry(connection).State = EntityState.Detached;
            return await Find() ?? throw new ApiException(409, "repository_revoked", "Folder configuration changed; try again.");
        }
        return connection;
    }

    public async Task<string?> SnapshotAsync(Guid organizationId, Guid projectId, Guid executionId, CancellationToken ct, Guid? connectionId = null)
    {
        var connection = connectionId is { } chosen
            ? await db.GitConnections.SingleOrDefaultAsync(g => g.Id == chosen && g.OrganizationId == organizationId
                && g.ProjectId == projectId && g.RevokedAt == null, ct)
                ?? throw new ApiException(409, "repository_revoked", "The task folder configuration was revoked.")
            : await db.GitConnections.SingleOrDefaultAsync(g => g.OrganizationId == organizationId
                && g.ProjectId == projectId && g.RevokedAt == null && !g.ForTasks, ct);
        if (connection is not null && (!GitRules.IsCommit(connection.BaseCommit) || string.IsNullOrWhiteSpace(connection.BaseBranch)
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
        else if (connection is { ForTasks: true })
        {
            // A folder is reused by many tasks: start from the branch as it is now. The connection version (its identity) is unchanged,
            // so executions already in review keep their own base commit and are checked against it.
            local.AuthorizedPath(connection.OrganizationId, connection.ProjectId, connection.LocalPath);
            var tip = await local.VerifyAsync(connection.LocalPath, connection.BaseBranch, ct);
            if (tip != connection.BaseCommit) { connection.BaseCommit = tip; await db.SaveChangesAsync(ct); }
        }
        return connection is null ? null : JsonSerializer.Serialize(new RepositorySnapshot(connection.Id, connection.Version,
            connection.RepositoryId, GitRules.Url(connection), connection.BaseBranch, connection.BaseCommit, $"teamflow/{executionId:N}", connection.LocalPath), JsonOptions);
    }

    public async Task<PublishedPullRequest> PublishAsync(Guid organizationId, Guid projectId, RepositorySnapshot snapshot,
        PublicationPlan plan, CancellationToken ct)
    {
        var connection = await RequireCurrentAsync(organizationId, projectId, snapshot, ct);
        var published = await publisher.PublishAsync(connection, plan, ct);
        await RequireUnchangedAsync(connection, snapshot, "Local connection changed during publication; reconcile its branch.", ct);
        return published;
    }

    public async Task<GitReviewSnapshot> ReviewAsync(Guid organizationId, Guid projectId, RepositorySnapshot snapshot,
        string expectedHead, CancellationToken ct)
    {
        var connection = await RequireCurrentAsync(organizationId, projectId, snapshot, ct);
        var result = await review.ReadAsync(connection, snapshot, expectedHead, ct);
        await RequireUnchangedAsync(connection, snapshot, "Local connection changed during review.", ct);
        return result;
    }

    public async Task MergeAsync(Guid organizationId, Guid projectId, RepositorySnapshot snapshot, string head, CancellationToken ct)
    {
        var connection = await RequireCurrentAsync(organizationId, projectId, snapshot, ct);
        await merge.MergeAsync(connection, snapshot, head, ct);
    }

    public async Task<string> LocalDiffAsync(Guid organizationId, Guid projectId, RepositorySnapshot snapshot, string head, CancellationToken ct)
    {
        var connection = await db.GitConnections.AsNoTracking().SingleOrDefaultAsync(g => g.Id == snapshot.ConnectionId
            && g.OrganizationId == organizationId && g.ProjectId == projectId && g.Version == snapshot.ConnectionVersion
            && g.RevokedAt == null, ct)
            ?? throw new ApiException(404, "repository_not_configured", "Active local connection not found.");
        var diff = await localGit.DiffAsync(connection, snapshot.BaseCommit, head, ct);
        await RequireUnchangedAsync(connection, snapshot, "Local connection changed during diff retrieval.", ct);
        return diff;
    }

    public async Task<CheckoutGrant> CheckoutGrantAsync(Guid executionId, Guid leaseToken, ClaimsPrincipal user, CancellationToken ct)
    {
        var execution = await leases.RequireAsync(executionId, leaseToken, user, ct);
        var snapshot = execution.RepositorySnapshot is null ? null : JsonSerializer.Deserialize<RepositorySnapshot>(execution.RepositorySnapshot, JsonOptions);
        if (snapshot is null) throw new ApiException(409, "repository_not_configured", "Execution has no repository snapshot.");
        var connection = await RequireCurrentAsync(execution.OrganizationId, execution.ProjectId, snapshot, ct);
        local.AuthorizedPath(connection.OrganizationId, connection.ProjectId, connection.LocalPath);
        if (await local.VerifyAsync(connection.LocalPath, connection.BaseBranch, ct) != snapshot.BaseCommit)
            throw new ApiException(409, "base_changed", "Local base branch changed; schedule a new validated execution.");
        await leases.RequireAsync(executionId, leaseToken, user, ct);
        await RequireUnchangedAsync(connection, snapshot, "Local connection changed during checkout authorization.", ct);
        return new("local", clock.GetUtcNow().AddSeconds(60));
    }

    private async Task<GitConnection> RequireCurrentAsync(Guid organizationId, Guid projectId, RepositorySnapshot snapshot, CancellationToken ct) =>
        await db.GitConnections.AsNoTracking().SingleOrDefaultAsync(g => g.Id == snapshot.ConnectionId
            && g.OrganizationId == organizationId && g.ProjectId == projectId && g.Version == snapshot.ConnectionVersion
            && g.RepositoryId == snapshot.RepositoryId && g.RevokedAt == null, ct)
        ?? throw new ApiException(409, "repository_revoked", "Repository connection was revoked or replaced.");

    // Revocation or replacement during Git I/O must also invalidate the result.
    private async Task RequireUnchangedAsync(GitConnection connection, RepositorySnapshot snapshot, string message, CancellationToken ct)
    {
        if (!await db.GitConnections.AnyAsync(g => g.Id == connection.Id && g.Version == snapshot.ConnectionVersion && g.RevokedAt == null, ct))
            throw new ApiException(409, "repository_revoked", message);
    }
}
