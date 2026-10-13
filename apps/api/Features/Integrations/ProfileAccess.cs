using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Integrations;

public sealed class ProfileAccess(AppDbContext db, IntegrationSecrets secrets, ITextModel model, IEmbeddingModel embeddings, CodexTextModel codex, IConfiguration configuration, CodexMachine machine)
{
    public Task<bool> IsLocalAsync(Guid organizationId, Guid projectId, Guid profileId, CancellationToken ct) =>
        db.AgentProfiles.AnyAsync(p => p.Id == profileId && p.OrganizationId == organizationId && p.ProjectId == projectId
            && p.RevokedAt == null && p.Provider == "codex", ct);

    public async Task<ProfileLimits> LimitsAsync(Guid organizationId, Guid projectId, Guid profileId, CancellationToken ct)
    {
        var profile = await db.AgentProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == profileId && p.OrganizationId == organizationId
            && p.ProjectId == projectId && p.RevokedAt == null, ct)
            ?? throw new ApiException(404, "profile_not_found", "Active project binding not found.");
        if (profile.Provider == "codex") machine.ApplySettings(profile);
        return new(profile.MaxInputBytes, profile.MaxOutputTokens, profile.InputUsdPerMillionTokens, profile.OutputUsdPerMillionTokens);
    }
    public async Task<EmbeddingResult> EmbedAsync(Guid organizationId, Guid projectId, Guid profileId, string[] inputs,
        decimal price, CancellationToken ct)
    {
        var profile = await db.AgentProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == profileId && p.OrganizationId == organizationId
            && p.ProjectId == projectId && p.RevokedAt == null, ct)
            ?? throw new ApiException(404, "profile_not_found", "Active project profile not found.");
        if (profile.Provider == "codex") throw new ApiException(409, "embedding_not_supported", "Codex CLI supports text generation; use textual memory search.");
        return await embeddings.EmbedAsync(secrets.Unprotect(organizationId, projectId, profile.Id, "openai", profile.ProtectedApiKey), inputs, price, ct);
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
        var profile = await db.AgentProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == profileId && p.OrganizationId == organizationId
            && p.ProjectId == projectId && p.RevokedAt == null, ct)
            ?? throw new ApiException(404, "profile_not_found", "Active project profile not found.");
        if (profile.Provider == "codex")
        {
            if (!configuration.GetValue<bool>("Codex:EnableRequests"))
                throw new ApiException(503, "integration_not_configured", "The project is not authorized for Codex CLI.");
            return await codex.GenerateAsync(machine.ApplySettings(profile), "", prompt, budgetUsd, ct);
        }
        var key = secrets.Unprotect(organizationId, projectId, profile.Id, "openai", profile.ProtectedApiKey);
        return await model.GenerateAsync(profile, key, prompt, budgetUsd, ct);
    }
}

public sealed record ProfileLimits(int MaxInputBytes, int MaxOutputTokens, decimal InputUsdPerMillionTokens, decimal OutputUsdPerMillionTokens);
