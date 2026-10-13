using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Auth;
using TeamFlow.Api.Features.Integrations;
using TeamFlow.Api.Features.Projects;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Memory;

public sealed class MemoryIndexAccess(AppDbContext db, MemoryAccess memory, ProjectAccess projects, ProfileAccess profiles,
    IConfiguration configuration, TimeProvider clock)
{
    public async Task<IndexDto> QueueAsync(Guid organizationId, Guid projectId, Guid itemId, IndexInput input, ClaimsPrincipal user, CancellationToken ct)
    {
        var source = await memory.RequireAsync(organizationId, projectId, itemId, user, ct);
        if (string.IsNullOrWhiteSpace(input.IdempotencyKey) || input.IdempotencyKey.Length is < 8 or > 128 || input.MaxCostUsd is <= 0 or > 5)
            throw new ApiException(400, "invalid_index_request", "Indexing requires an idempotency key and budget up to USD 5.");
        var hash = Hash(JsonSerializer.Serialize(new { itemId, input.Version, input.ProfileId, input.MaxCostUsd }));
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({itemId.ToString()}))", ct);
        var existing = await db.MemoryIndexes.SingleOrDefaultAsync(i => i.OrganizationId == organizationId && i.ProjectId == projectId
            && i.ItemId == itemId && i.IdempotencyKey == input.IdempotencyKey, ct);
        if (existing is not null)
        {
            if (existing.RequestHash != hash) throw new ApiException(409, "index_key_conflict", "Idempotency key already has a different request.");
            return ToDto(existing);
        }
        if (source.Version != input.Version) throw new ApiException(409, "memory_version_conflict", "Reload the source before indexing.");
        if (source.Status != MemoryStatus.Validated) throw new ApiException(409, "memory_not_validated", "Validate the source before indexing.");
        await profiles.RequireActiveAsync(organizationId, projectId, input.ProfileId, ct);
        var textOnly = await profiles.IsLocalAsync(organizationId, projectId, input.ProfileId, ct);
        var job = new MemoryIndex
        {
            Id = Guid.NewGuid(),
            Model = textOnly ? "text-only" : "text-embedding-3-small",
            Dimensions = textOnly ? 0 : 1536,
            OrganizationId = organizationId,
            ProjectId = projectId,
            ItemId = itemId,
            SourceVersion = source.Version,
            ProfileId = input.ProfileId,
            RequestedBy = AuthEndpoints.UserId(user),
            IdempotencyKey = input.IdempotencyKey,
            RequestHash = hash,
            MaxCostUsd = input.MaxCostUsd,
            CreatedAt = clock.GetUtcNow()
        };
        db.MemoryIndexes.Add(job);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ToDto(job);
    }

    public async Task<bool> ProcessAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await db.MemoryIndexes.Where(i => (i.State == "processing" || i.State == "calling") && i.LeaseUntil < now)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.State, "uncertain").SetProperty(i => i.FailureCode, "index_interrupted"), ct);
        MemoryIndex? job;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            job = await db.MemoryIndexes.FromSqlRaw("SELECT * FROM \"MemoryIndexes\" WHERE \"State\" = 'queued' ORDER BY \"CreatedAt\", \"Id\" FOR UPDATE SKIP LOCKED LIMIT 1").SingleOrDefaultAsync(ct);
            if (job is null) return false;
            job.State = "processing"; job.Lease = Guid.NewGuid(); job.LeaseUntil = now.AddMinutes(2);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        var called = false;
        try
        {
            var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, job.RequestedBy.ToString())], "memory-index"));
            var source = await memory.RequireAsync(job.OrganizationId, job.ProjectId, job.ItemId, user, ct);
            if (source.Version != job.SourceVersion || source.Status != MemoryStatus.Validated)
                throw new ApiException(409, "index_source_changed", "Source is no longer the validated requested version.");
            MemoryAccess.ValidateContent(source.Title, source.Content);
            MemoryAccess.ValidateSource(source.Key, source.Kind);
            await profiles.RequireActiveAsync(job.OrganizationId, job.ProjectId, job.ProfileId, ct);
            var price = configuration.GetValue<decimal>("Memory:EmbeddingUsdPerMillionTokens");
            var textOnly = job.Model == "text-only";
            if (!textOnly && (price <= 0 || !configuration.GetValue<bool>("Memory:EnableEmbeddingRequests") || !configuration.GetValue<bool>("OpenAI:EnableRequests")))
                throw new ApiException(503, "provider_disabled", "Explicit enablement and embedding price are required.");
            var parts = Split(source.Content);
            job.ReservedUsd = textOnly ? 0 : (Encoding.UTF8.GetByteCount(source.Content) + parts.Length * 1024m) * price / 1_000_000m;
            if (job.ReservedUsd > job.MaxCostUsd) throw new ApiException(409, "budget_exceeded", "Embedding estimate exceeds the request budget.");
            job.State = "calling";
            await db.SaveChangesAsync(ct);
            called = !textOnly;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            var result = textOnly ? new EmbeddingResult(parts.Select(_ => Array.Empty<float>()).ToArray(), 0, 0)
                : await profiles.EmbedAsync(job.OrganizationId, job.ProjectId, job.ProfileId, parts, price, linked.Token);
            if (result.Vectors.Length != parts.Length || result.Vectors.Any(v => v.Length != job.Dimensions || v.Any(f => !float.IsFinite(f)) || (!textOnly && v.All(f => f == 0))))
                throw new ApiException(502, "embedding_invalid_response", "Embedding dimensions could not be verified.");
            job.InputTokens = result.InputTokens; job.EstimatedCostUsd = result.EstimatedCostUsd;
            // Recheck authorization and live version after network I/O; derivatives never grant access.
            await projects.RequireAsync(job.OrganizationId, job.ProjectId, user, linked.Token);
            await profiles.RequireActiveAsync(job.OrganizationId, job.ProjectId, job.ProfileId, linked.Token);
            var current = await db.MemoryItems.AsNoTracking().SingleAsync(m => m.Id == source.Id, linked.Token);
            if (current.ArchivedAt != null || current.Version != job.SourceVersion || current.Status != MemoryStatus.Validated)
                throw new ApiException(409, "index_source_changed", "Source changed during indexing.");
            for (var i = 0; i < parts.Length; i++) db.MemoryChunks.Add(new() { IndexId = job.Id, Sequence = i, Content = parts[i], ContentHash = Hash(parts[i]), Embedding = result.Vectors[i] });
            job.State = "completed"; job.CompletedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(linked.Token);
        }
        catch (Exception error) when (error is ApiException or HttpRequestException or OperationCanceledException)
        {
            job.State = called ? "uncertain" : "failed";
            job.FailureCode = error is ApiException api ? api.Code : "embedding_unavailable";
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await db.SaveChangesAsync(cleanup.Token);
        }
        return true;
    }

    public static string[] Split(string content)
    {
        var chunks = new List<string>(); var part = new StringBuilder(); var bytes = 0;
        foreach (var rune in content.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > 2048) { chunks.Add(part.ToString()); part.Clear(); bytes = 0; }
            part.Append(rune.ToString()); bytes += rune.Utf8SequenceLength;
        }
        if (part.Length > 0) chunks.Add(part.ToString());
        return chunks.ToArray();
    }
    private static string Hash(string content) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    public static IndexDto ToDto(MemoryIndex i) => new(i.Id, i.ItemId, i.SourceVersion, i.Model, i.Dimensions, i.Chunker, i.State,
        i.FailureCode, i.CreatedAt, i.CompletedAt, i.MaxCostUsd, i.ReservedUsd, i.EstimatedCostUsd, i.InputTokens);
}

public sealed class MemoryIndexWorker(IServiceScopeFactory scopes, IConfiguration configuration, IHostEnvironment environment,
    ILogger<MemoryIndexWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (environment.IsEnvironment("Testing") || !configuration.GetValue<bool>("Memory:EnableIndexWorker")) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                if (!await scope.ServiceProvider.GetRequiredService<MemoryIndexAccess>().ProcessAsync(stoppingToken))
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                logger.LogError("Memory indexing stopped for retry; error type {ErrorType}", error.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }
}
