using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Projects;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Integrations;

public sealed class ProfileService(AppDbContext db, ProjectAccess projects, LocalCliAgents agents, CodexMachine machine, TimeProvider clock)
{
    public async Task<CliAgent[]> ListAgentsAsync(Guid organizationId, Guid projectId, ClaimsPrincipal user, CancellationToken ct)
    {
        await projects.RequireAsync(organizationId, projectId, user, ct);
        return await agents.ListAsync(ct);
    }

    public async Task<CodexMachineStatus> MachineStatusAsync(Guid organizationId, Guid projectId, ClaimsPrincipal user, CancellationToken ct)
    {
        await projects.RequireAsync(organizationId, projectId, user, ct);
        return await machine.StatusAsync(ct);
    }

    public async Task<AgentProfile> EnsureMachineBindingAsync(Guid organizationId, Guid projectId, ClaimsPrincipal user, CancellationToken ct)
    {
        await projects.RequireAsync(organizationId, projectId, user, ct, owner: true);
        return await machine.EnsureBindingAsync(organizationId, projectId, ct);
    }

    public async Task<PageResult<ProfileDto>> ListAsync(Guid organizationId, Guid projectId, int? page, int? pageSize, ClaimsPrincipal user, CancellationToken ct)
    {
        await projects.RequireAsync(organizationId, projectId, user, ct);
        var (number, size) = Pagination.Validate(page, pageSize);
        var query = db.AgentProfiles.AsNoTracking().Where(p => p.OrganizationId == organizationId && p.ProjectId == projectId);
        var total = await query.CountAsync(ct);
        var profiles = await query.OrderBy(p => p.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
        return new PageResult<ProfileDto>(profiles.Select(ProfileDto.From).ToList(), number, size, total);
    }

    public async Task RevokeAsync(Guid organizationId, Guid projectId, Guid id, ClaimsPrincipal user, CancellationToken ct)
    {
        await projects.RequireAsync(organizationId, projectId, user, ct, owner: true);
        var count = await db.AgentProfiles.Where(p => p.Id == id && p.OrganizationId == organizationId && p.ProjectId == projectId)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.RevokedAt, clock.GetUtcNow()), ct);
        if (count == 0) throw new ApiException(404, "profile_not_found", "Profile not found.");
    }
}
