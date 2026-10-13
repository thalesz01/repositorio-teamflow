using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Projects;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Memory;

public sealed record ContextSource(Guid ItemId, Guid Version, Guid IndexId, int Sequence, string Key, string Title,
    string Content, string ContentHash, string SourceHash, string? Commit, string? PullRequestUrl);
public sealed record ContextBundle(int SchemaVersion, string Query, ContextSource[] Sources, int Bytes, string[] Warnings);
public sealed record SearchInput(string Query, int MaxBytes = 8192);
public sealed class SearchRow
{
    public Guid ItemId { get; set; }
    public Guid Version { get; set; }
    public Guid IndexId { get; set; }
    public int Sequence { get; set; }
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string SourceHash { get; set; } = "";
    public string? Commit { get; set; }
    public string? PullRequestUrl { get; set; }
    public double Score { get; set; }
}

public sealed class MemorySearch(AppDbContext db, ProjectAccess projects)
{
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);
    public async Task<ContextBundle> FindAsync(Guid organizationId, Guid projectId, SearchInput input, ClaimsPrincipal user, CancellationToken ct)
    {
        await projects.RequireAsync(organizationId, projectId, user, ct);
        var query = Input.Required(input.Query, "query", 1000);
        if (input.MaxBytes is < 2048 or > 16384)
            throw new ApiException(400, "invalid_search", "Context requires a bounded query and a context budget of 2048..16384 bytes.");
        // Tenant, source status, live version and completed index are filtered inside SQL before ranking.
        var rows = await db.Database.SqlQuery<SearchRow>($"""
            SELECT m."Id" AS "ItemId", m."Version", i."Id" AS "IndexId", c."Sequence", m."Key", m."Title",
              c."Content", c."ContentHash", m."ContentHash" AS "SourceHash", m."Commit", m."PullRequestUrl",
              ts_rank(to_tsvector('simple', m."Title" || ' ' || c."Content"), plainto_tsquery('simple', {query}))::double precision AS "Score"
            FROM "MemoryChunks" c JOIN "MemoryIndexes" i ON i."Id" = c."IndexId"
              JOIN "MemoryItems" m ON m."Id" = i."ItemId" AND m."OrganizationId" = i."OrganizationId" AND m."ProjectId" = i."ProjectId"
            WHERE m."OrganizationId" = {organizationId} AND m."ProjectId" = {projectId} AND m."ArchivedAt" IS NULL
              AND m."Status" = 'Validated' AND m."Version" = i."SourceVersion" AND i."State" = 'completed'
              AND NOT EXISTS (SELECT 1 FROM "MemoryIndexes" newer WHERE newer."ItemId" = i."ItemId"
                AND newer."SourceVersion" = i."SourceVersion" AND newer."State" = 'completed'
                AND (newer."CreatedAt", newer."Id") > (i."CreatedAt", i."Id"))
              AND to_tsvector('simple', m."Title" || ' ' || c."Content") @@ plainto_tsquery('simple', {query})
            ORDER BY "Score" DESC, m."UpdatedAt" DESC, m."Id", c."Sequence" LIMIT 20
            """).ToListAsync(ct);
        var sources = new List<ContextSource>(); var itemIds = new HashSet<Guid>();
        foreach (var row in rows)
        {
            if (sources.Count == 10 || itemIds.Count == 5 && !itemIds.Contains(row.ItemId)) continue;
            MemoryAccess.ValidateContent(row.Title, row.Content);
            var source = new ContextSource(row.ItemId, row.Version, row.IndexId, row.Sequence, row.Key, row.Title,
                row.Content, row.ContentHash, row.SourceHash, row.Commit, row.PullRequestUrl);
            if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(sources.Append(source), JsonOptions)) > input.MaxBytes - 256) continue;
            itemIds.Add(row.ItemId); sources.Add(source);
        }
        var result = new ContextBundle(1, query, sources.ToArray(), 0,
            sources.Count == 0 ? ["No current validated indexed source matched. Clarify the demand rather than inventing project facts."] : []);
        for (var i = 0; i < 3; i++) result = result with { Bytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(result, JsonOptions)) };
        if (result.Bytes > input.MaxBytes) throw new ApiException(400, "context_limit", "Query and citations exceed the context budget.");
        return result;
    }

    public async Task VerifyAsync(Guid organizationId, Guid projectId, ContextBundle bundle, ClaimsPrincipal user, CancellationToken ct)
    {
        await projects.RequireAsync(organizationId, projectId, user, ct);
        if (bundle.SchemaVersion != 1 || bundle.Sources.Length > 10) throw new ApiException(409, "context_invalid", "Invalid source package.");
        foreach (var source in bundle.Sources)
        {
            var valid = await (from chunk in db.MemoryChunks.AsNoTracking()
                               join index in db.MemoryIndexes.AsNoTracking() on chunk.IndexId equals index.Id
                               join item in db.MemoryItems.AsNoTracking() on index.ItemId equals item.Id
                               where item.OrganizationId == organizationId && item.ProjectId == projectId && index.OrganizationId == organizationId && index.ProjectId == projectId
                                 && item.Id == source.ItemId && item.Version == source.Version && item.ContentHash == source.SourceHash
                                 && item.Key == source.Key && item.Title == source.Title && item.Commit == source.Commit && item.PullRequestUrl == source.PullRequestUrl
                                 && item.Status == MemoryStatus.Validated && item.ArchivedAt == null && index.State == "completed" && index.SourceVersion == source.Version
                                 && index.Id == source.IndexId && chunk.Sequence == source.Sequence && chunk.ContentHash == source.ContentHash && chunk.Content == source.Content
                               select chunk.IndexId).AnyAsync(ct);
            if (!valid) throw new ApiException(409, "context_source_changed", "A referenced source was changed, archived or revoked; analyze again.");
        }
    }
}
