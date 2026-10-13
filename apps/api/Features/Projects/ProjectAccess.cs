using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Organizations;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Projects;

public sealed class ProjectAccess(AppDbContext db, OrganizationAccess organizations)
{
    public async Task<Project> RequireAsync(Guid organizationId, Guid projectId, ClaimsPrincipal user, CancellationToken ct, bool owner = false)
    {
        await organizations.RequireAsync(organizationId, user, ct, owner);
        return await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId && p.OrganizationId == organizationId, ct)
            ?? throw new ApiException(404, "project_not_found", "Project not found.");
    }
}
