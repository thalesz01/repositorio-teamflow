using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Projects;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Integrations;

public static partial class ProfileEndpoints
{
    public static ProfileDto ToDto(AgentProfile profile) => new(profile.Id, profile.Name, profile.Provider, profile.Model,
        ["textGeneration"], [], profile.MaxInputBytes, profile.MaxOutputTokens, profile.TimeoutSeconds,
        profile.InputUsdPerMillionTokens, profile.OutputUsdPerMillionTokens, profile.RevokedAt);

    public static void MapProfiles(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/organizations/{organizationId:guid}/projects/{projectId:guid}/profiles")
            .RequireAuthorization().WithTags("Agent profiles");
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
        group.MapPost("/", async (Guid organizationId, Guid projectId, ProfileInput input, ClaimsPrincipal user,
            ProjectAccess access, AppDbContext db, IntegrationSecrets secrets, CancellationToken ct) =>
        {
            await access.RequireAsync(organizationId, projectId, user, ct, owner: true);
            var name = Input.Required(input.Name, "name", 120);
            var model = Input.Required(input.Model, "model", 120);
            if (!ModelName().IsMatch(model) || input.MaxInputBytes is < 256 or > 65536
                || input.MaxOutputTokens is < 16 or > 8192 || input.TimeoutSeconds is < 5 or > 120
                || !ValidPrice(input.InputUsdPerMillionTokens) || !ValidPrice(input.OutputUsdPerMillionTokens))
                throw new ApiException(400, "invalid_profile", "Invalid model, limits or configured token prices.");
            var key = secrets.Configured(organizationId, projectId, "OpenAI:ApiKey");
            if (key.Length is < 8 or > 1024 || key.Any(char.IsWhiteSpace))
                throw new ApiException(503, "invalid_integration_configuration", "The server integration credential is invalid.");
            var profile = new AgentProfile
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                ProjectId = projectId,
                Name = name,
                Model = model,
                MaxInputBytes = input.MaxInputBytes,
                MaxOutputTokens = input.MaxOutputTokens,
                TimeoutSeconds = input.TimeoutSeconds,
                InputUsdPerMillionTokens = input.InputUsdPerMillionTokens,
                OutputUsdPerMillionTokens = input.OutputUsdPerMillionTokens
            };
            profile.ProtectedApiKey = secrets.Protect(organizationId, projectId, profile.Id, "openai", key);
            db.AgentProfiles.Add(profile);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(profile));
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
                .ExecuteUpdateAsync(set => set.SetProperty(p => p.RevokedAt, clock.GetUtcNow()).SetProperty(p => p.ProtectedApiKey, ""), ct);
            if (count == 0) throw new ApiException(404, "profile_not_found", "Profile not found.");
            return Results.NoContent();
        });
    }

    private static bool ValidPrice(decimal price) => price is > 0 and <= 1000 && decimal.Round(price, 6) == price;
    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9._:-]{0,119}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ModelName();
}
