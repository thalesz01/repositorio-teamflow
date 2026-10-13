using TeamFlow.Api.Features.Runners;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Executions;

public sealed class LeaseAccess(AppDbContext db, TimeProvider clock, RunnerAccess runners)
{
    public async Task<Execution> RequireRecordedOwnerAsync(Guid id, Guid token, ClaimsPrincipal user, CancellationToken ct)
    {
        var runnerId = Guid.Parse(user.FindFirstValue("runnerId") ?? "");
        var workerId = Guid.Parse(user.FindFirstValue("workerId") ?? "");
        var organizationId = Guid.Parse(user.FindFirstValue("organizationId") ?? "");
        var projectId = Guid.Parse(user.FindFirstValue("projectId") ?? "");
        await runners.RequireActiveAsync(organizationId, projectId, runnerId, workerId, ct);
        return await db.Executions.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id && e.LeaseToken == token
            && e.RunnerCredentialId == runnerId && e.WorkerId == workerId && e.OrganizationId == organizationId && e.ProjectId == projectId, ct)
            ?? throw new ApiException(409, "lease_lost", "Recorded execution ownership does not match.");
    }

    public async Task<Execution> LockRecordedOwnerAsync(Guid id, Guid token, ClaimsPrincipal user, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Lease lock requires a transaction.");
        var execution = await RequireRecordedOwnerAsync(id, token, user, ct);
        await db.RunnerCredentials.FromSqlInterpolated($"SELECT * FROM \"RunnerCredentials\" WHERE \"Id\" = {execution.RunnerCredentialId} FOR SHARE")
            .AsNoTracking().ToListAsync(ct);
        await db.Executions.FromSqlInterpolated($"SELECT * FROM \"Executions\" WHERE \"Id\" = {id} FOR UPDATE")
            .AsNoTracking().ToListAsync(ct);
        return await RequireRecordedOwnerAsync(id, token, user, ct);
    }

    public async Task<Execution> LockAsync(Guid id, Guid token, ClaimsPrincipal user, CancellationToken ct)
    {
        await LockRecordedOwnerAsync(id, token, user, ct);
        return await RequireAsync(id, token, user, ct);
    }

    public async Task<Execution> RequireAsync(Guid executionId, Guid leaseToken, ClaimsPrincipal user, CancellationToken ct)
    {
        var runnerId = Guid.Parse(user.FindFirstValue("runnerId") ?? throw new ApiException(401, "runner_required", "Runner identity is required."));
        var workerId = Guid.Parse(user.FindFirstValue("workerId") ?? throw new ApiException(401, "runner_required", "Runner identity is required."));
        var organizationId = Guid.Parse(user.FindFirstValue("organizationId") ?? "");
        var projectId = Guid.Parse(user.FindFirstValue("projectId") ?? "");
        await runners.RequireActiveAsync(organizationId, projectId, runnerId, workerId, ct);
        var now = clock.GetUtcNow();
        return await db.Executions.AsNoTracking().SingleOrDefaultAsync(e => e.Id == executionId && e.OrganizationId == organizationId
            && e.ProjectId == projectId && e.RunnerCredentialId == runnerId && e.WorkerId == workerId && e.LeaseToken == leaseToken
            && e.State == ExecutionState.Leased && !e.CancellationRequested && e.LeaseExpiresAt > now && e.DeadlineAt > now, ct)
            ?? throw new ApiException(409, "lease_lost", "Execution ownership is no longer valid.");
    }
}
