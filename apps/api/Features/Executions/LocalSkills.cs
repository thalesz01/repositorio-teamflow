using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TeamFlow.Api.Features.Integrations;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Executions;

public sealed record SkillInfo(string Id, string Name, string Description, string Source, int Bytes, string Preview);
public sealed record SkillSnapshot(string Id, string Name, string Source, string Hash, string Content);

/// <summary>
/// Skills are SKILL.md instruction files. The person picks one for an execution; its text is stored with that execution and
/// sent to the model next to the task. A skill never widens the controller's tool policy.
/// </summary>
public sealed partial class LocalSkills(IConfiguration configuration, LocalRepository local)
{
    // The prompt carries the skill on every model turn, so it stays well below the profile's input limit.
    public const int MaxBytes = 12 * 1024;
    private const int MaxPerRoot = 100;

    public IReadOnlyList<SkillInfo> List(Guid organizationId, Guid projectId, string? folder)
    {
        var skills = new List<SkillInfo>();
        foreach (var (kind, root) in Roots(organizationId, projectId, folder))
        {
            if (!Directory.Exists(root) || new DirectoryInfo(root).LinkTarget is not null) continue;
            foreach (var directory in new DirectoryInfo(root).EnumerateDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).Take(MaxPerRoot))
            {
                if (!NameRule().IsMatch(directory.Name) || Read(directory.FullName) is not { } text) continue;
                var (name, description) = Header(text, directory.Name);
                skills.Add(new($"{kind}:{directory.Name}", name, description, kind.StartsWith("project", StringComparison.Ordinal) ? "project" : "user",
                    Encoding.UTF8.GetByteCount(text), Body(text)));
            }
        }
        return skills;
    }

    public SkillSnapshot Resolve(Guid organizationId, Guid projectId, string? folder, string id)
    {
        var separator = id.IndexOf(':');
        var kind = separator > 0 ? id[..separator] : "";
        var directory = separator > 0 ? id[(separator + 1)..] : "";
        var root = Roots(organizationId, projectId, folder).SingleOrDefault(item => item.Kind == kind).Root;
        if (root is null || !NameRule().IsMatch(directory) || !Directory.Exists(root) || new DirectoryInfo(root).LinkTarget is not null
            || Read(Path.Combine(root, directory)) is not { } text)
            throw new ApiException(400, "invalid_skill", "The selected skill is not available for this task.");
        var (name, _) = Header(text, directory);
        return new(id, name, kind.StartsWith("project", StringComparison.Ordinal) ? "project" : "user",
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))), text);
    }

    // Project skills come from the task's own folder; personal ones from this machine's user profile. Nothing else is read.
    private IEnumerable<(string Kind, string Root)> Roots(Guid organizationId, Guid projectId, string? folder)
    {
        if (!string.IsNullOrWhiteSpace(folder))
        {
            var path = local.AuthorizedPath(organizationId, projectId, folder, requireGit: false);
            yield return ("project-claude", Path.Combine(path, ".claude", "skills"));
            yield return ("project-agents", Path.Combine(path, ".agents", "skills"));
        }
        if (!configuration.GetValue("Local:UserSkills", true)) yield break;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return ("user-claude", Path.Combine(home, ".claude", "skills"));
        yield return ("user-codex", Path.Combine(Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(home, ".codex"), "skills"));
    }

    // Only a regular SKILL.md inside the skill's own folder, small and valid UTF-8, is accepted.
    private static string? Read(string directory)
    {
        var folder = new DirectoryInfo(directory);
        if (!folder.Exists || folder.LinkTarget is not null) return null;
        var file = new FileInfo(Path.Combine(directory, "SKILL.md"));
        if (!file.Exists || file.LinkTarget is not null || file.Length is 0 or > MaxBytes) return null;
        try { return new UTF8Encoding(false, true).GetString(File.ReadAllBytes(file.FullName)); }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or DecoderFallbackException) { return null; }
    }

    private static (string Name, string Description) Header(string text, string fallback)
    {
        string name = fallback, description = "";
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length > 0 && lines[0].Trim() == "---")
            foreach (var line in lines.Skip(1))
            {
                if (line.Trim() == "---") break;
                var colon = line.IndexOf(':');
                if (colon <= 0) continue;
                var value = line[(colon + 1)..].Trim().Trim('"', '\'');
                if (value.Length == 0) continue;
                if (line.StartsWith("name:", StringComparison.Ordinal)) name = Cap(value, 80);
                else if (line.StartsWith("description:", StringComparison.Ordinal)) description = Cap(value, 300);
            }
        return (name, description);
    }

    private static string Body(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var start = lines.Length > 0 && lines[0].Trim() == "---" ? Array.FindIndex(lines, 1, line => line.Trim() == "---") + 1 : 0;
        return Cap(string.Join('\n', lines.Skip(Math.Max(start, 0))).Trim(), 500);
    }

    private static string Cap(string value, int length) => value.Length > length ? value[..length] : value;

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex NameRule();
}
