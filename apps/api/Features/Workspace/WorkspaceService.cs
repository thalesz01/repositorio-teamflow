using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Auth;
using TeamFlow.Api.Features.Organizations;
using TeamFlow.Api.Features.Projects;

namespace TeamFlow.Api.Features.Workspace;

// The interface no longer asks for a team or a project: each person works in one of their own projects (ADR 0010).
// Isolation still uses organizationId/projectId underneath.
public sealed class WorkspaceService(AppDbContext db)
{
    public async Task<WorkspaceDto> ResolveAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var userId = AuthEndpoints.UserId(user);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Two tabs opening at once must not create two personal workspaces.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({"workspace:" + userId}))", ct);
        var workspace = await FindOwnedAsync(userId, ct) ?? await CreatePersonalAsync(userId, ct);
        await transaction.CommitAsync(ct);
        return workspace;
    }

    // Existing work stays reachable: prefer the owned project with the newest task, then the first owned project.
    private async Task<WorkspaceDto?> FindOwnedAsync(Guid userId, CancellationToken ct)
    {
        var owned = from member in db.Memberships.AsNoTracking()
                    join project in db.Projects.AsNoTracking() on member.OrganizationId equals project.OrganizationId
                    where member.UserId == userId && member.Role == MemberRole.Owner
                    select project;
        return await (from task in db.WorkItems.AsNoTracking()
                      join project in owned on new { task.OrganizationId, task.ProjectId } equals new { project.OrganizationId, ProjectId = project.Id }
                      orderby task.CreatedAt descending
                      select new WorkspaceDto(project.OrganizationId, project.Id)).FirstOrDefaultAsync(ct)
            ?? await owned.OrderBy(project => project.Name).ThenBy(project => project.Id)
                .Select(project => new WorkspaceDto(project.OrganizationId, project.Id)).FirstOrDefaultAsync(ct);
    }

    private async Task<WorkspaceDto> CreatePersonalAsync(Guid userId, CancellationToken ct)
    {
        var organization = new Organization { Id = Guid.NewGuid(), Name = "Pessoal" };
        var project = new Project { Id = Guid.NewGuid(), OrganizationId = organization.Id, Name = "Tarefas" };
        db.Organizations.Add(organization);
        db.Memberships.Add(new Membership { OrganizationId = organization.Id, UserId = userId, Role = MemberRole.Owner });
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        return new WorkspaceDto(organization.Id, project.Id);
    }
}
