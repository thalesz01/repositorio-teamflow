using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Projects;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Integrations;

public static class ProfileEndpoints
{
    public static ProfileDto ToDto(AgentProfile profile) => new(profile.Id, profile.Name, profile.Provider, profile.Model,
        ["textGeneration"], [], profile.MaxInputBytes, profile.MaxOutputTokens, profile.TimeoutSeconds,
        profile.InputUsdPerMillionTokens, profile.OutputUsdPerMillionTokens, profile.RevokedAt);

    public static void MapProfiles(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/organizations/{organizationId:guid}/projects/{projectId:guid}/profiles")
            .RequireAuthorization().WithTags("Agent profiles");
        group.MapGet("/agents", async (Guid organizationId, Guid projectId, ClaimsPrincipal user, ProjectAccess access, LocalCliAgents agents, CancellationToken ct) =>
        {
            await access.RequireAsync(organizationId, projectId, user, ct);
            return Results.Ok(await agents.ListAsync(ct));
        }).Produces<CliAgent[]>();
        group.MapGet("/machine", async (Guid organizationId, Guid projectId, ClaimsPrincipal user, ProjectAccess access, CodexMachine machine, CancellationToken ct) =>
        {
            await access.RequireAsync(organizationId, projectId, user, ct);
            return Results.Ok(await machine.StatusAsync(ct));
        }).Produces<CodexMachineStatus>();
        group.MapPost("/machine", async (Guid organizationId, Guid projectId, ClaimsPrincipal user, ProjectAccess access, CodexMachine machine, CancellationToken ct) =>
        {
            await access.RequireAsync(organizationId, projectId, user, ct, owner: true);
            return Results.Ok(ToDto(await machine.EnsureBindingAsync(organizationId, projectId, ct)));
        }).Produces<ProfileDto>();
        group.MapPost("/codex", async (Guid organizationId, Guid projectId, ClaimsPrincipal user, ProjectAccess access, CancellationToken ct) =>
        {
            await access.RequireAsync(organizationId, projectId, user, ct, owner: true);
            return Results.Problem(statusCode: 410, title: "Use the authenticated machine Codex; project profiles are no longer configurable.");
        });
        group.MapGet("/", async (Guid organizationId, Guid projectId, int? page, int? pageSize, ClaimsPrincipal user,
            ProjectAccess access, AppDbContext db, CancellationToken ct) =>
        {
            await access.RequireAsync(organizationId, projectId, user, ct);
            var (number, size) = Pagination.Validate(page, pageSize);
            var query = db.AgentProfiles.AsNoTracking().Where(p => p.OrganizationId == organizationId && p.ProjectId == projectId);
            var total = await query.CountAsync(ct);
            var profiles = await query.OrderBy(p => p.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return Results.Ok(new PageResult<ProfileDto>(profiles.Select(ToDto).ToList(), number, size, total));
        }).Produces<PageResult<ProfileDto>>();
        group.MapDelete("/{id:guid}", async (Guid organizationId, Guid projectId, Guid id, ClaimsPrincipal user,
            ProjectAccess access, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            await access.RequireAsync(organizationId, projectId, user, ct, owner: true);
            var count = await db.AgentProfiles.Where(p => p.Id == id && p.OrganizationId == organizationId && p.ProjectId == projectId)
                .ExecuteUpdateAsync(set => set.SetProperty(p => p.RevokedAt, clock.GetUtcNow()), ct);
            if (count == 0) throw new ApiException(404, "profile_not_found", "Profile not found.");
            return Results.NoContent();
        });
    }
}
