using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Integrations;

public sealed record AgentChoice(string Provider = "codex", string Model = "default", string? Effort = null);
public sealed record CliModel(string Id, string[] Efforts);
public sealed record CliAgent(string Id, string Name, bool Installed, bool Authenticated, bool Enabled, CliModel[] Models);

public sealed class LocalCliAgents(IConfiguration configuration, CodexMachine codex, AppDbContext db)
{
    private static readonly CliModel[] ClaudeModels = [new("default", []), new("sonnet", ["low", "medium", "high"]), new("opus", ["low", "medium", "high"]), new("haiku", [])];

    public async Task<CliAgent[]> ListAsync(CancellationToken ct) =>
        [await StatusAsync("codex", "Codex", CodexModels(), ct), await StatusAsync("claude", "Claude Code", ClaudeModels, ct)];

    // Choosing an AI while writing a task only needs the catalog; whether it can run is checked when the task is executed.
    private IEnumerable<(string Id, string Name, CliModel[] Models)> Catalog() => [("codex", "Codex", CodexModels()), ("claude", "Claude Code", ClaudeModels)];

    /// <summary>Fails with cli_not_ready unless the CLI is installed, logged in and enabled on this machine.</summary>
    public async Task RequireReadyAsync(string provider, CancellationToken ct)
    {
        var agent = provider switch { "codex" => await StatusAsync("codex", "Codex", [], ct), "claude" => await StatusAsync("claude", "Claude Code", [], ct), _ => throw InvalidSelection() };
        if (!agent.Installed || !agent.Authenticated || !agent.Enabled)
            throw new ApiException(503, "cli_not_ready", "Install, authenticate and enable the selected CLI.");
    }

    private async Task<CliAgent> StatusAsync(string id, string name, CliModel[] models, CancellationToken ct)
    {
        if (id == "codex")
        {
            var status = await codex.StatusAsync(ct);
            return new(id, name, status.Installed, status.Authenticated, status.Enabled, models);
        }
        var executable = configuration["Claude:Executable"] ?? "";
        var installed = LocalCliProcess.Installed(executable); var authenticated = false;
        if (installed)
        {
            try { authenticated = (await LocalCliProcess.RunAsync(executable, ["auth", "status"], null, 10, ct)).ExitCode == 0; }
            catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException) { ct.ThrowIfCancellationRequested(); }
        }
        return new(id, name, installed, authenticated, configuration.GetValue<bool>("Claude:EnableRequests"), models);
    }

    private CliModel[] CodexModels()
    {
        var cache = configuration["Codex:ModelsCachePath"] ?? Path.Combine(Environment.GetEnvironmentVariable("CODEX_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"), "models_cache.json");
        var models = new List<CliModel> { new("default", []) };
        try
        {
            if (!File.Exists(cache) || new FileInfo(cache).Length > 2_097_152) return models.ToArray();
            using var document = JsonDocument.Parse(File.ReadAllText(cache));
            foreach (var entry in document.RootElement.GetProperty("models").EnumerateArray().Take(50))
            {
                var id = entry.GetProperty("slug").GetString();
                if (id is null || !System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-zA-Z0-9._-]{1,100}$")) continue;
                if (entry.TryGetProperty("visibility", out var visibility) && visibility.GetString() != "list") continue;
                var efforts = entry.GetProperty("supported_reasoning_levels").EnumerateArray().Select(item => item.GetProperty("effort").GetString())
                    .Where(value => value is "low" or "medium" or "high" or "xhigh" or "max").Cast<string>().ToArray();
                models.Add(new(id, efforts));
            }
        }
        catch (Exception error) when (error is IOException or JsonException or KeyNotFoundException or InvalidOperationException) { }
        return models.DistinctBy(model => model.Id).ToArray();
    }

    public async Task<AgentProfile> SelectAsync(Guid organizationId, Guid projectId, AgentChoice choice, CancellationToken ct)
    {
        var agent = Catalog().SingleOrDefault(item => item.Id == choice.Provider);
        if (agent.Id is null) throw InvalidSelection();
        var model = agent.Models.SingleOrDefault(item => item.Id == choice.Model) ?? throw InvalidSelection();
        if (choice.Effort is not null && !model.Efforts.Contains(choice.Effort)) throw InvalidSelection();
        var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"cli-selection:{organizationId}:{projectId}:{choice.Provider}:{choice.Model}:{choice.Effort}"))[..16]);
        var existing = await db.AgentProfiles.SingleOrDefaultAsync(item => item.Id == id, ct);
        if (existing is not null && existing.RevokedAt is null) return existing;
        if (existing is not null) throw new ApiException(409, "profile_revoked", "This selection was revoked.");
        var profile = new AgentProfile
        {
            Id = id,
            OrganizationId = organizationId,
            ProjectId = projectId,
            Provider = choice.Provider,
            Name = agent.Name + " · " + choice.Model,
            Model = choice.Model,
            ReasoningEffort = choice.Effort,
            IsTaskSelection = true,
            MaxInputBytes = configuration.GetValue(choice.Provider == "codex" ? "Codex:MaxInputBytes" : "Claude:MaxInputBytes", 32768),
            MaxOutputTokens = 8192,
            TimeoutSeconds = configuration.GetValue(choice.Provider == "codex" ? "Codex:TimeoutSeconds" : "Claude:TimeoutSeconds", 120)
        };
        // Concurrent selections must reuse the same immutable record, never change settings under a running task.
        db.AgentProfiles.Add(profile);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException error) when (error.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        { db.Entry(profile).State = EntityState.Detached; return await db.AgentProfiles.SingleAsync(item => item.Id == id && item.OrganizationId == organizationId && item.ProjectId == projectId && item.RevokedAt == null, ct); }
        return profile;
    }
    private static ApiException InvalidSelection() => new(400, "invalid_agent_selection", "Select an available provider, model and effort combination.");
}
