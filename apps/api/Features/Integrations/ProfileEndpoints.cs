using System.Security.Claims;
using TeamFlow.Api.Features.Projects;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Integrations;

public static class ProfileEndpoints
{
    public static void MapProfiles(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/organizations/{organizationId:guid}/projects/{projectId:guid}/profiles")
            .RequireAuthorization().WithTags("Agent profiles");
        group.MapGet("/agents", ListAgentsAsync).Produces<CliAgent[]>();
        group.MapGet("/machine", MachineStatusAsync).Produces<CodexMachineStatus>();
        group.MapPost("/machine", EnsureMachineBindingAsync).Produces<ProfileDto>();
        group.MapPost("/codex", RetiredCodexAsync);
        group.MapGet("/", ListAsync).Produces<PageResult<ProfileDto>>();
        group.MapDelete("/{id:guid}", RevokeAsync);
    }

    private static async Task<IResult> ListAgentsAsync(Guid organizationId, Guid projectId, ClaimsPrincipal user, ProfileService service, CancellationToken ct) =>
        Results.Ok(await service.ListAgentsAsync(organizationId, projectId, user, ct));

    private static async Task<IResult> MachineStatusAsync(Guid organizationId, Guid projectId, ClaimsPrincipal user, ProfileService service, CancellationToken ct) =>
        Results.Ok(await service.MachineStatusAsync(organizationId, projectId, user, ct));

    private static async Task<IResult> EnsureMachineBindingAsync(Guid organizationId, Guid projectId, ClaimsPrincipal user, ProfileService service, CancellationToken ct) =>
        Results.Ok(ProfileDto.From(await service.EnsureMachineBindingAsync(organizationId, projectId, user, ct)));

    private static async Task<IResult> RetiredCodexAsync(Guid organizationId, Guid projectId, ClaimsPrincipal user, ProjectAccess access, CancellationToken ct)
    {
        await access.RequireAsync(organizationId, projectId, user, ct, owner: true);
        return Results.Problem(statusCode: 410, title: "Use the authenticated machine Codex; project profiles are no longer configurable.");
    }

    private static async Task<IResult> ListAsync(Guid organizationId, Guid projectId, int? page, int? pageSize, ClaimsPrincipal user, ProfileService service, CancellationToken ct) =>
        Results.Ok(await service.ListAsync(organizationId, projectId, page, pageSize, user, ct));

    private static async Task<IResult> RevokeAsync(Guid organizationId, Guid projectId, Guid id, ClaimsPrincipal user, ProfileService service, CancellationToken ct)
    {
        await service.RevokeAsync(organizationId, projectId, id, user, ct);
        return Results.NoContent();
    }
}
