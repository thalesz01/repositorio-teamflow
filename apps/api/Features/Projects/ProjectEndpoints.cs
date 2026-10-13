using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Organizations;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Projects;

public sealed record ProjectDto(Guid Id, Guid OrganizationId, string Name, Guid Version);
public sealed record ProjectInput(string Name);
public sealed record ProjectUpdate(string Name, Guid Version);

public static class ProjectEndpoints
{
    public static void MapProjects(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/organizations/{organizationId:guid}/projects").WithTags("Projects").RequireAuthorization();
        group.MapGet("/", ListAsync).Produces<PageResult<ProjectDto>>();
        group.MapPost("/", CreateAsync).Produces<ProjectDto>(201);
        group.MapGet("/{projectId:guid}", async (Guid organizationId, Guid projectId, ClaimsPrincipal user, ProjectAccess access, CancellationToken ct) =>
            ToDto(await access.RequireAsync(organizationId, projectId, user, ct)));
        group.MapPut("/{projectId:guid}", UpdateAsync).Produces<ProjectDto>();
        group.MapDelete("/{projectId:guid}", DeleteAsync);
    }

    private static ProjectDto ToDto(Project project) => new(project.Id, project.OrganizationId, project.Name, project.Version);

    private static async Task<IResult> ListAsync(Guid organizationId, int? page, int? pageSize, ClaimsPrincipal user, OrganizationAccess access, AppDbContext db, CancellationToken ct)
    {
        await access.RequireAsync(organizationId, user, ct);
        var (number, size) = Pagination.Validate(page, pageSize);
        var query = db.Projects.AsNoTracking().Where(p => p.OrganizationId == organizationId).OrderBy(p => p.Name).ThenBy(p => p.Id);
        var items = await query.Skip((number - 1) * size).Take(size).Select(p => new ProjectDto(p.Id, p.OrganizationId, p.Name, p.Version)).ToListAsync(ct);
        return Results.Ok(new PageResult<ProjectDto>(items, number, size, await query.CountAsync(ct)));
    }

    private static async Task<IResult> CreateAsync(Guid organizationId, ProjectInput request, ClaimsPrincipal user, OrganizationAccess access, AppDbContext db, CancellationToken ct)
    {
        await access.RequireAsync(organizationId, user, ct, owner: true);
        var project = new Project { Id = Guid.NewGuid(), OrganizationId = organizationId, Name = Input.Required(request.Name, "name", 120) };
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/organizations/{organizationId}/projects/{project.Id}", ToDto(project));
    }

    private static async Task<IResult> UpdateAsync(Guid organizationId, Guid projectId, ProjectUpdate request, ClaimsPrincipal user, ProjectAccess access, AppDbContext db, CancellationToken ct)
    {
        var project = await access.RequireAsync(organizationId, projectId, user, ct, owner: true);
        if (project.Version != request.Version)
            throw new ApiException(409, "version_conflict", "Project has changed. Reload before editing.");
        project.Name = Input.Required(request.Name, "name", 120);
        project.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(project));
    }

    private static async Task<IResult> DeleteAsync(Guid organizationId, Guid projectId, Guid version, ClaimsPrincipal user, ProjectAccess access, AppDbContext db, CancellationToken ct)
    {
        var project = await access.RequireAsync(organizationId, projectId, user, ct, owner: true);
        if (project.Version != version)
            throw new ApiException(409, "version_conflict", "Project has changed. Reload before deleting.");
        db.Projects.Remove(project);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
