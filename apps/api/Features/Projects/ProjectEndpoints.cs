using System.Security.Claims;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Projects;

public static class ProjectEndpoints
{
    public static void MapProjects(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/organizations/{organizationId:guid}/projects").WithTags("Projects").RequireAuthorization();
        group.MapGet("/", ListAsync).Produces<PageResult<ProjectDto>>();
        group.MapPost("/", CreateAsync).Produces<ProjectDto>(201);
        group.MapGet("/{projectId:guid}", GetAsync).Produces<ProjectDto>();
        group.MapPut("/{projectId:guid}", UpdateAsync).Produces<ProjectDto>();
        group.MapDelete("/{projectId:guid}", DeleteAsync);
    }

    private static async Task<IResult> ListAsync(Guid organizationId, int? page, int? pageSize, ClaimsPrincipal user, ProjectService service, CancellationToken ct) =>
        Results.Ok(await service.ListAsync(organizationId, page, pageSize, user, ct));

    private static async Task<IResult> GetAsync(Guid organizationId, Guid projectId, ClaimsPrincipal user, ProjectAccess access, CancellationToken ct) =>
        Results.Ok(ProjectDto.From(await access.RequireAsync(organizationId, projectId, user, ct)));

    private static async Task<IResult> CreateAsync(Guid organizationId, ProjectInput request, ClaimsPrincipal user, ProjectService service, CancellationToken ct)
    {
        var project = await service.CreateAsync(organizationId, request, user, ct);
        return Results.Created($"/api/v1/organizations/{organizationId}/projects/{project.Id}", ProjectDto.From(project));
    }

    private static async Task<IResult> UpdateAsync(Guid organizationId, Guid projectId, ProjectUpdate request, ClaimsPrincipal user, ProjectService service, CancellationToken ct) =>
        Results.Ok(ProjectDto.From(await service.UpdateAsync(organizationId, projectId, request, user, ct)));

    private static async Task<IResult> DeleteAsync(Guid organizationId, Guid projectId, Guid version, ClaimsPrincipal user, ProjectService service, CancellationToken ct)
    {
        await service.DeleteAsync(organizationId, projectId, version, user, ct);
        return Results.NoContent();
    }
}
