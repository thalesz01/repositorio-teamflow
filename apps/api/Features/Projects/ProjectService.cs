using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Organizations;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Projects;

public sealed class ProjectService(AppDbContext db, ProjectAccess projects, OrganizationAccess organizations)
{
    public async Task<PageResult<ProjectDto>> ListAsync(Guid organizationId, int? page, int? pageSize, ClaimsPrincipal user, CancellationToken ct)
    {
        await organizations.RequireAsync(organizationId, user, ct);
        var (number, size) = Pagination.Validate(page, pageSize);
        var query = db.Projects.AsNoTracking().Where(p => p.OrganizationId == organizationId).OrderBy(p => p.Name).ThenBy(p => p.Id);
        var items = await query.Skip((number - 1) * size).Take(size).Select(p => new ProjectDto(p.Id, p.OrganizationId, p.Name, p.Version)).ToListAsync(ct);
        return new PageResult<ProjectDto>(items, number, size, await query.CountAsync(ct));
    }

    public async Task<Project> CreateAsync(Guid organizationId, ProjectInput request, ClaimsPrincipal user, CancellationToken ct)
    {
        await organizations.RequireAsync(organizationId, user, ct, owner: true);
        var project = new Project { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = Input.Required(request.Name, "name", 120) };
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        return project;
    }

    public async Task<Project> UpdateAsync(Guid organizationId, Guid projectId, ProjectUpdate request, ClaimsPrincipal user, CancellationToken ct)
    {
        var project = await projects.RequireAsync(organizationId, projectId, user, ct, owner: true);
        if (project.Version != request.Version)
            throw new ApiException(409, "version_conflict", "Project has changed. Reload before editing.");
        project.Name = Input.Required(request.Name, "name", 120);
        project.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return project;
    }

    public async Task DeleteAsync(Guid organizationId, Guid projectId, Guid version, ClaimsPrincipal user, CancellationToken ct)
    {
        var project = await projects.RequireAsync(organizationId, projectId, user, ct, owner: true);
        if (project.Version != version)
            throw new ApiException(409, "version_conflict", "Project has changed. Reload before deleting.");
        db.Projects.Remove(project);
        await db.SaveChangesAsync(ct);
    }
}
