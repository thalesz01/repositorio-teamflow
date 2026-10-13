using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Runners;

public sealed class RunnerAccess(AppDbContext db, TimeProvider clock)
{
    public async Task RequireActiveAsync(Guid organizationId, Guid projectId, Guid runnerId, Guid workerId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (!await db.RunnerCredentials.AnyAsync(r => r.Id == runnerId && r.WorkerId == workerId
            && r.OrganizationId == organizationId && r.ProjectId == projectId && r.RevokedAt == null && r.ExpiresAt > now, ct))
            throw new ApiException(401, "runner_revoked", "Runner credential is no longer valid.");
    }
}
