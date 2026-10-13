using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Integrations;

public sealed class ProfileAccess(AppDbContext db, ITextModel model, IConfiguration configuration, CodexMachine machine)
{
    public async Task<ProfileLimits> LimitsAsync(Guid organizationId, Guid projectId, Guid profileId, CancellationToken ct)
    {
        var profile = await ActiveAsync(organizationId, projectId, profileId, "Active project binding not found.", ct);
        return new(profile.MaxInputBytes, profile.MaxOutputTokens, profile.InputUsdPerMillionTokens, profile.OutputUsdPerMillionTokens);
    }

    public async Task RequireActiveAsync(Guid organizationId, Guid projectId, Guid profileId, CancellationToken ct)
    {
        if (!await db.AgentProfiles.AnyAsync(p => p.Id == profileId && p.OrganizationId == organizationId
            && p.ProjectId == projectId && p.RevokedAt == null, ct))
            throw new ApiException(404, "profile_not_found", "Active project profile not found.");
    }

    public async Task<TextModelResult> GenerateAsync(Guid organizationId, Guid projectId, Guid profileId,
        string prompt, decimal budgetUsd, CancellationToken ct)
    {
        var profile = await ActiveAsync(organizationId, projectId, profileId, "Active project profile not found.", ct);
        if (!configuration.GetValue<bool>("Codex:EnableRequests"))
            throw new ApiException(503, "integration_not_configured", "The project is not authorized for Codex CLI.");
        return await model.GenerateAsync(profile, prompt, budgetUsd, ct);
    }

    // Settings come from the machine's Codex configuration, never from stored project values.
    private async Task<AgentProfile> ActiveAsync(Guid organizationId, Guid projectId, Guid profileId, string missing, CancellationToken ct)
    {
        var profile = await db.AgentProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == profileId && p.OrganizationId == organizationId
            && p.ProjectId == projectId && p.RevokedAt == null, ct)
            ?? throw new ApiException(404, "profile_not_found", missing);
        if (profile.Provider != "codex") throw new ApiException(409, "profile_unsupported", "Only Codex CLI profiles are supported.");
        return machine.ApplySettings(profile);
    }
}

public sealed record ProfileLimits(int MaxInputBytes, int MaxOutputTokens, decimal InputUsdPerMillionTokens, decimal OutputUsdPerMillionTokens);
