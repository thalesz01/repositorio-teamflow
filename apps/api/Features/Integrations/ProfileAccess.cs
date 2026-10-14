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
        string prompt, decimal budgetUsd, CancellationToken ct, string? responseSchema = null)
    {
        var profile = await ActiveAsync(organizationId, projectId, profileId, "Active project profile not found.", ct);
        if (!configuration.GetValue<bool>(profile.Provider == "codex" ? "Codex:EnableRequests" : "Claude:EnableRequests"))
            throw new ApiException(503, "integration_not_configured", "The selected CLI is disabled.");
        return await model.GenerateAsync(profile, prompt, budgetUsd, ct, responseSchema);
    }

    // Settings come from the machine's Codex configuration, never from stored project values.
    private async Task<AgentProfile> ActiveAsync(Guid organizationId, Guid projectId, Guid profileId, string missing, CancellationToken ct)
    {
        var profile = await db.AgentProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == profileId && p.OrganizationId == organizationId
            && p.ProjectId == projectId && p.RevokedAt == null, ct)
            ?? throw new ApiException(404, "profile_not_found", missing);
        if (profile.Provider is not ("codex" or "claude")) throw new ApiException(409, "profile_unsupported", "Unsupported CLI profile.");
        if (!profile.IsTaskSelection) return machine.ApplySettings(profile);
        // The input limit belongs to the machine, not to the stored selection: analyses read many files and need more room than 32 KiB.
        profile.MaxInputBytes = configuration.GetValue(profile.Provider == "claude" ? "Claude:MaxInputBytes" : "Codex:MaxInputBytes", 98304);
        return profile;
    }
}

public sealed record ProfileLimits(int MaxInputBytes, int MaxOutputTokens, decimal InputUsdPerMillionTokens, decimal OutputUsdPerMillionTokens);
