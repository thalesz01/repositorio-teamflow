using System.Security.Claims;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Organizations;

public static class OrganizationEndpoints
{
    public static void MapOrganizations(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/organizations").WithTags("Organizations").RequireAuthorization();
        group.MapGet("/", ListAsync).Produces<PageResult<OrganizationDto>>();
        group.MapPost("/", CreateAsync).Produces<OrganizationDto>(201);
        group.MapGet("/{organizationId:guid}/members", ListMembersAsync).Produces<PageResult<MemberDto>>();
        group.MapPost("/{organizationId:guid}/members", AddMemberAsync).Produces<MemberDto>(201);
        group.MapDelete("/{organizationId:guid}/members/{userId:guid}", RemoveMemberAsync);
    }

    private static async Task<IResult> ListAsync(int? page, int? pageSize, ClaimsPrincipal user, OrganizationService service, CancellationToken ct) =>
        Results.Ok(await service.ListAsync(page, pageSize, user, ct));

    private static async Task<IResult> CreateAsync(OrganizationInput request, ClaimsPrincipal user, OrganizationService service, CancellationToken ct)
    {
        var organization = await service.CreateAsync(request, user, ct);
        return Results.Created($"/api/v1/organizations/{organization.Id}", organization);
    }

    private static async Task<IResult> ListMembersAsync(Guid organizationId, int? page, int? pageSize, ClaimsPrincipal user, OrganizationService service, CancellationToken ct) =>
        Results.Ok(await service.ListMembersAsync(organizationId, page, pageSize, user, ct));

    private static async Task<IResult> AddMemberAsync(Guid organizationId, MemberInput request, ClaimsPrincipal user, OrganizationService service, CancellationToken ct)
    {
        var member = await service.AddMemberAsync(organizationId, request, user, ct);
        return Results.Created($"/api/v1/organizations/{organizationId}/members/{member.UserId}", member);
    }

    private static async Task<IResult> RemoveMemberAsync(Guid organizationId, Guid userId, ClaimsPrincipal user, OrganizationService service, CancellationToken ct)
    {
        await service.RemoveMemberAsync(organizationId, userId, user, ct);
        return Results.NoContent();
    }
}
