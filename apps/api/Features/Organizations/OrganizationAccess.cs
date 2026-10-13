using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Auth;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Organizations;

public sealed class OrganizationAccess(AppDbContext db)
{
    public async Task RequireAsync(Guid organizationId, ClaimsPrincipal user, CancellationToken cancellationToken, bool owner = false)
    {
        var userId = AuthEndpoints.UserId(user);
        var role = await db.Memberships.Where(m => m.OrganizationId == organizationId && m.UserId == userId)
            .Select(m => m.Role).SingleOrDefaultAsync(cancellationToken);
        if (role is null)
            throw new ApiException(404, "organization_not_found", "Organization not found.");
        if (owner && role != "owner")
            throw new ApiException(403, "owner_required", "Owner permission is required.");
    }

    public Task<bool> IsMemberAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
        db.Memberships.AnyAsync(m => m.OrganizationId == organizationId && m.UserId == userId, cancellationToken);
}
