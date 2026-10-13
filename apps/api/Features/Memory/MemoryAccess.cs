using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Auth;
using TeamFlow.Api.Features.Executions;
using TeamFlow.Api.Features.Projects;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Memory;

public sealed partial class MemoryAccess(AppDbContext db, ProjectAccess projects, ExecutionKnowledge knowledge, TimeProvider clock)
{
    public async Task<MemoryDetail> CreateAsync(Guid organizationId, Guid projectId, MemoryInput input, ClaimsPrincipal user, CancellationToken ct)
    {
        await projects.RequireAsync(organizationId, projectId, user, ct);
        ValidateContent(input.Title, input.Content);
        ValidateSource(input.Key, input.Kind);
        if (!Enum.IsDefined(input.Kind) || input.Kind == MemoryKind.Resolution || string.IsNullOrWhiteSpace(input.Key) || input.Key.Length is < 1 or > 240 || input.Key.Contains('\\') || input.Key.Contains(':')
            || input.Key.StartsWith('/') || input.Key.Split('/').Any(s => s.Length == 0 || s.StartsWith('.'))
            || input.Key.EndsWith(".pem", StringComparison.OrdinalIgnoreCase) || input.Key.EndsWith(".key", StringComparison.OrdinalIgnoreCase))
            throw new ApiException(400, "invalid_memory_source", "Invalid source key or reserved resolution kind.");
        var item = new MemoryItem
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ProjectId = projectId,
            Key = input.Key,
            Title = input.Title.Trim(),
            Content = input.Content,
            Kind = input.Kind,
            Status = MemoryStatus.Candidate,
            CreatedBy = AuthEndpoints.UserId(user),
            CreatedAt = clock.GetUtcNow()
        };
        db.MemoryItems.Add(item);
        Record(item, item.CreatedBy);
        await db.SaveChangesAsync(ct);
        return Detail(item);
    }

    public async Task<MemoryDetail> FromExecutionAsync(Guid organizationId, Guid projectId, Guid executionId, ClaimsPrincipal user, CancellationToken ct)
    {
        await projects.RequireAsync(organizationId, projectId, user, ct);
        return await ExtractAsync(organizationId, projectId, executionId, AuthEndpoints.UserId(user), ct);
    }

    internal async Task<MemoryDetail> ExtractAsync(Guid organizationId, Guid projectId, Guid executionId, Guid actor, CancellationToken ct)
    {
        var source = await knowledge.ReadAsync(organizationId, projectId, executionId, ct);
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({executionId.ToString()}))", ct);
        var existing = await db.MemoryItems.SingleOrDefaultAsync(m => m.OrganizationId == organizationId && m.ProjectId == projectId && m.ExecutionId == executionId, ct);
        if (existing?.ArchivedAt is not null) throw new ApiException(409, "memory_archived", "Restore the archived source explicitly.");
        if (existing is not null) return Detail(existing);
        var title = Sensitive().IsMatch(source.Title) ? "Resolution " + executionId.ToString("N")[..12] : source.Title;
        var content = Sensitive().Replace(source.Content, "[redacted]");
        var bounded = new StringBuilder();
        var bytes = 0;
        foreach (var rune in content.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > 32000) { bounded.Append("\n[truncated; consult the linked task/PR for complete evidence]"); break; }
            bounded.Append(rune.ToString()); bytes += rune.Utf8SequenceLength;
        }
        content = bounded.ToString();
        ValidateContent(title, content);
        var item = new MemoryItem
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ProjectId = projectId,
            Key = "resolution/" + executionId.ToString("N"),
            Title = title,
            Content = content,
            Kind = MemoryKind.Resolution,
            Status = MemoryStatus.Candidate,
            CreatedBy = source.AuthorId,
            CreatedAt = clock.GetUtcNow(),
            TaskId = source.TaskId,
            ExecutionId = source.ExecutionId,
            PullRequestUrl = source.PullRequestUrl,
            Commit = source.Commit
        };
        db.MemoryItems.Add(item);
        Record(item, actor);
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return Detail(item);
    }

    public async Task<MemoryItem> RequireAsync(Guid organizationId, Guid projectId, Guid id, ClaimsPrincipal user, CancellationToken ct, bool archived = false)
    {
        await projects.RequireAsync(organizationId, projectId, user, ct);
        return await db.MemoryItems.SingleOrDefaultAsync(m => m.Id == id && m.OrganizationId == organizationId && m.ProjectId == projectId
            && (archived || m.ArchivedAt == null), ct) ?? throw new ApiException(404, "memory_not_found", "Memory source not found.");
    }

    public async Task<MemoryDetail> UpdateAsync(Guid organizationId, Guid projectId, Guid id, MemoryUpdate input, ClaimsPrincipal user, CancellationToken ct)
    {
        var item = await RequireAsync(organizationId, projectId, id, user, ct);
        if (item.Version != input.Version) throw Conflict();
        ValidateContent(input.Title, input.Content);
        ValidateSource(item.Key, item.Kind);
        if (!Enum.IsDefined(input.Status)) throw new ApiException(400, "invalid_memory_status", "Unknown memory status.");
        item.Title = input.Title.Trim(); item.Content = input.Content; item.Status = input.Status;
        Record(item, AuthEndpoints.UserId(user));
        await db.SaveChangesAsync(ct);
        return Detail(item);
    }

    public async Task<MemoryDetail> ArchiveAsync(Guid organizationId, Guid projectId, Guid id, Guid version, bool restore, ClaimsPrincipal user, CancellationToken ct)
    {
        var item = await RequireAsync(organizationId, projectId, id, user, ct, archived: true);
        if (item.Version != version) throw Conflict();
        item.ArchivedAt = restore ? null : clock.GetUtcNow();
        if (restore) item.Status = MemoryStatus.Candidate;
        Record(item, AuthEndpoints.UserId(user));
        await db.SaveChangesAsync(ct);
        return Detail(item);
    }

    private void Record(MemoryItem item, Guid actor)
    {
        item.Version = Guid.NewGuid(); item.UpdatedAt = clock.GetUtcNow(); item.UpdatedBy = actor;
        item.ContentHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(item.Content)));
        db.MemoryRevisions.Add(new()
        {
            ItemId = item.Id,
            OrganizationId = item.OrganizationId,
            ProjectId = item.ProjectId,
            Version = item.Version,
            Title = item.Title,
            Content = item.Content,
            ContentHash = item.ContentHash,
            Status = item.Status,
            Archived = item.ArchivedAt != null,
            AuthorId = actor,
            CreatedAt = item.UpdatedAt
        });
    }
    public static void ValidateSource(string key, MemoryKind kind)
    {
        if (kind == MemoryKind.Resolution && key?.StartsWith("resolution/", StringComparison.Ordinal) == true) return;
        var segments = key?.Split('/') ?? [];
        string[] excluded = ["node_modules", "bin", "obj", "vendor", "credentials", "secrets", "appsettings", "package-lock", "packages.lock"];
        string[] extensions = [".md", ".txt", ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".sql", ".html", ".css", ".json", ".yaml", ".yml"];
        if (segments.Length == 0 || segments.Any(s => s.Length == 0 || s.StartsWith('.') || excluded.Any(e => s.Equals(e, StringComparison.OrdinalIgnoreCase)
            || s.StartsWith(e + ".", StringComparison.OrdinalIgnoreCase))) || key is null || key.Contains('\\') || key.Contains(':')
            || !extensions.Any(e => key.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
            throw new ApiException(400, "invalid_memory_source", "Use an explicitly allowed document or code path without secrets or generated files.");
    }
    public static void ValidateContent(string title, string content)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 160 || string.IsNullOrWhiteSpace(content) || Encoding.UTF8.GetByteCount(content) > 32768)
            throw new ApiException(400, "invalid_memory_content", "Memory requires a title and content limited to 32 KiB.");
        if (Sensitive().IsMatch(title + "\n" + content))
            throw new ApiException(400, "sensitive_memory_content", "Remove credential-like content before storing a memory source.");
    }
    [GeneratedRegex(@"(?i)(-----BEGIN [A-Z ]*PRIVATE KEY-----|\b(?:sk|ghp|github_pat)[-_][A-Za-z0-9_-]{16,}|\bBearer\s+[A-Za-z0-9._~-]{16,}|\b(?:password|api[_-]?key|secret|access[_-]?token)[""']?\s*[:=]\s*[""']?[^\s<*""']{4,})")]
    private static partial Regex Sensitive();
    private static ApiException Conflict() => new(409, "memory_version_conflict", "Source changed; reload before editing.");
    public static MemoryDto ToDto(MemoryItem m) => new(m.Id, m.Key, m.Title, m.Kind, m.Status, m.Version, m.ContentHash,
        m.CreatedBy, m.UpdatedBy, m.CreatedAt, m.UpdatedAt, m.ArchivedAt, m.TaskId, m.ExecutionId, m.PullRequestUrl, m.Commit);
    public static MemoryDetail Detail(MemoryItem item) => new(ToDto(item), item.Content);
}
