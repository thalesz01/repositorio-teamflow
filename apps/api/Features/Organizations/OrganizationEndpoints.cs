using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Auth;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Organizations;

public sealed record OrganizationDto(Guid Id, string Name, string Role);
public sealed record OrganizationInput(string Name);
public sealed record MemberDto(Guid UserId, string Email, string Role);
public sealed record MemberInput(string Email);

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

    private static async Task<IResult> ListAsync(int? page, int? pageSize, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var (number, size) = Pagination.Validate(page, pageSize);
        var userId = AuthEndpoints.UserId(user);
        var query = from membership in db.Memberships.AsNoTracking()
                    join organization in db.Organizations on membership.OrganizationId equals organization.Id
                    where membership.UserId == userId
                    orderby organization.Name, organization.Id
                    select new OrganizationDto(organization.Id, organization.Name, membership.Role);
        return Results.Ok(new PageResult<OrganizationDto>(await query.Skip((number - 1) * size).Take(size).ToListAsync(ct), number, size, await query.CountAsync(ct)));
    }

    private static async Task<IResult> CreateAsync(OrganizationInput request, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var organization = new Organization { Id = Guid.NewGuid(), Name = Input.Required(request.Name, "name", 120) };
        db.Organizations.Add(organization);
        db.Memberships.Add(new Membership { OrganizationId = organization.Id, UserId = AuthEndpoints.UserId(user), Role = "owner" });
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/organizations/{organization.Id}", new OrganizationDto(organization.Id, organization.Name, "owner"));
    }

    private static async Task<IResult> ListMembersAsync(Guid organizationId, int? page, int? pageSize, ClaimsPrincipal user, OrganizationAccess access, AppDbContext db, CancellationToken ct)
    {
        await access.RequireAsync(organizationId, user, ct);
        var (number, size) = Pagination.Validate(page, pageSize);
        var query = from member in db.Memberships.AsNoTracking()
                    join account in db.Users on member.UserId equals account.Id
                    where member.OrganizationId == organizationId
                    orderby account.Email, member.UserId
                    select new MemberDto(member.UserId, account.Email ?? "", member.Role);
        return Results.Ok(new PageResult<MemberDto>(await query.Skip((number - 1) * size).Take(size).ToListAsync(ct), number, size, await query.CountAsync(ct)));
    }

    private static async Task<IResult> AddMemberAsync(Guid organizationId, MemberInput request, ClaimsPrincipal user, OrganizationAccess access, AppDbContext db, UserManager<AppUser> users, CancellationToken ct)
    {
        await access.RequireAsync(organizationId, user, ct, owner: true);
        var account = await users.FindByEmailAsync(Input.Required(request.Email, "email", 254))
            ?? throw new ApiException(404, "user_not_found", "Registered user not found.");
        if (await access.IsMemberAsync(organizationId, account.Id, ct))
            throw new ApiException(409, "already_member", "User is already a member.");
        db.Memberships.Add(new Membership { OrganizationId = organizationId, UserId = account.Id });
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/organizations/{organizationId}/members/{account.Id}", new MemberDto(account.Id, account.Email ?? "", "member"));
    }

    private static async Task<IResult> RemoveMemberAsync(Guid organizationId, Guid userId, ClaimsPrincipal user, OrganizationAccess access, AppDbContext db, CancellationToken ct)
    {
        await access.RequireAsync(organizationId, user, ct, owner: true);
        var member = await db.Memberships.SingleOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == userId, ct)
            ?? throw new ApiException(404, "member_not_found", "Member not found.");
        if (member.Role == "owner")
            throw new ApiException(409, "owner_removal_forbidden", "The organization owner cannot be removed.");
        db.Memberships.Remove(member);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
