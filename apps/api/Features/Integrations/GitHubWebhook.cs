using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Integrations;

public sealed class GitHubDelivery
{
    public int Attempts { get; set; }
    public Guid? Lease { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public string? FailureCode { get; set; }
    public Guid Id { get; set; }
    public Guid ConnectionId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid DeliveryId { get; set; }
    public string Event { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public string Payload { get; set; } = "";
    public string State { get; set; } = "pending";
    public DateTimeOffset ReceivedAt { get; set; }
}

public static class WebhookSignature
{
    public static bool Valid(ReadOnlySpan<byte> body, string? signature, string secret)
    {
        if (signature is null || signature.Length != 71 || !signature.StartsWith("sha256=", StringComparison.Ordinal)) return false;
        byte[] received;
        try { received = Convert.FromHexString(signature.AsSpan(7)); }
        catch (FormatException) { return false; }
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);
        return CryptographicOperations.FixedTimeEquals(expected, received);
    }
}

public sealed class WebhookInbox(AppDbContext db, IConfiguration configuration, TimeProvider clock)
{
    public async Task AcceptAsync(byte[] body, string? signature, string? eventName, string? delivery,
        CancellationToken ct)
    {
        var secret = configuration["GitHub:WebhookSecret"];
        if (secret is null || secret.Length is < 16 or > 256)
            throw new ApiException(503, "webhook_not_configured", "Configure the webhook secret on this server.");
        if (body.Length > 128 * 1024) throw new ApiException(413, "webhook_too_large", "Webhook exceeds the payload limit.");
        if (!WebhookSignature.Valid(body, signature, secret))
            throw new ApiException(401, "invalid_webhook_signature", "Invalid webhook signature.");
        if (!Guid.TryParse(delivery, out var deliveryId) || deliveryId == Guid.Empty)
            throw new ApiException(400, "invalid_webhook_delivery", "Invalid delivery identifier.");
        if (eventName == "ping") return;
        if (eventName is not ("pull_request" or "pull_request_review" or "check_run" or "check_suite" or "push")) return;
        long repositoryId;
        long installationId;
        string json;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("repository", out var repository)
                || !repository.TryGetProperty("id", out var repoId) || !repoId.TryGetInt64(out repositoryId) || repositoryId < 1
                || !document.RootElement.TryGetProperty("installation", out var installation)
                || !installation.TryGetProperty("id", out var installId) || !installId.TryGetInt64(out installationId) || installationId < 1)
                throw new ApiException(400, "invalid_webhook_payload", "Repository and installation identity are required.");
            json = document.RootElement.GetRawText();
        }
        catch (JsonException) { throw new ApiException(400, "invalid_webhook_payload", "Webhook payload must be valid JSON."); }
        var hash = Convert.ToHexStringLower(SHA256.HashData(body));
        // Fan-out is explicit: a repository can be connected to several authorized projects.
        var connections = await db.GitConnections.AsNoTracking().Where(g => g.RepositoryId == repositoryId
            && g.InstallationId == installationId && g.RevokedAt == null).Select(g => new { g.Id, g.OrganizationId, g.ProjectId }).Take(101).ToListAsync(ct);
        if (connections.Count > 100) throw new ApiException(409, "webhook_scope_limit", "Webhook exceeds the supported project fan-out.");
        if (connections.Count == 0) throw new ApiException(404, "webhook_repository_not_found", "No active authorized connection matches this delivery.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var connection in connections)
        {
            // Unique constraint reconciles concurrent deliveries without relying on process memory.
            var id = Guid.NewGuid();
            var received = clock.GetUtcNow();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"GitHubDeliveries\" (\"Id\", \"ConnectionId\", \"OrganizationId\", \"ProjectId\", \"DeliveryId\", \"Event\", \"PayloadHash\", \"Payload\", \"State\", \"ReceivedAt\") VALUES ({id}, {connection.Id}, {connection.OrganizationId}, {connection.ProjectId}, {deliveryId}, {eventName}, {hash}, CAST({json} AS jsonb), 'pending', {received}) ON CONFLICT (\"ConnectionId\", \"DeliveryId\") DO NOTHING", ct);
            var existing = await db.GitHubDeliveries.AsNoTracking().SingleAsync(d => d.ConnectionId == connection.Id && d.DeliveryId == deliveryId, ct);
            if (existing.PayloadHash != hash || existing.Event != eventName)
                throw new ApiException(409, "webhook_delivery_conflict", "Delivery identifier was reused with a different payload or event.");
        }
        await transaction.CommitAsync(ct);
    }
}

public static class WebhookEndpoints
{
    public static void MapGitHubWebhooks(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/webhooks/github", async (HttpRequest request, WebhookInbox inbox, CancellationToken ct) =>
        {
            using var body = new MemoryStream();
            var buffer = new byte[8192];
            while (true)
            {
                var count = await request.Body.ReadAsync(buffer, ct);
                if (count == 0) break;
                if (body.Length + count > 128 * 1024) throw new ApiException(413, "webhook_too_large", "Webhook exceeds the payload limit.");
                await body.WriteAsync(buffer.AsMemory(0, count), ct);
            }
            await inbox.AcceptAsync(body.ToArray(), request.Headers["X-Hub-Signature-256"].FirstOrDefault(),
                request.Headers["X-GitHub-Event"].FirstOrDefault(), request.Headers["X-GitHub-Delivery"].FirstOrDefault(), ct);
            return Results.Accepted();
        }).AllowAnonymous().WithTags("Webhooks");
    }
}
