using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Executions;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Integrations;

public sealed class WebhookProcessor(AppDbContext db, ReviewAccess reviews, TimeProvider clock)
{
    public async Task<bool> ProcessOneAsync(CancellationToken ct)
    {
        GitHubDelivery delivery;
        var lease = Guid.NewGuid();
        var now = clock.GetUtcNow();
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await db.GitHubDeliveries.Where(d => d.State == "processing" && d.LeaseUntil <= now && d.Attempts >= 3)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.State, "failed").SetProperty(d => d.FailureCode, "webhook_attempt_limit"), ct);
            var rows = await db.GitHubDeliveries.FromSqlInterpolated($"""
                SELECT * FROM "GitHubDeliveries" WHERE "Attempts" < 3 AND
                ("State" = 'pending' OR ("State" = 'processing' AND "LeaseUntil" <= {now}))
                ORDER BY "ReceivedAt", "Id" LIMIT 1 FOR UPDATE SKIP LOCKED
                """).ToListAsync(ct);
            if (rows.Count == 0) return false;
            delivery = rows[0];
            delivery.State = "processing";
            delivery.Attempts++;
            delivery.Lease = lease;
            delivery.LeaseUntil = now.AddMinutes(2);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            var connection = await db.GitConnections.AsNoTracking().SingleOrDefaultAsync(g => g.Id == delivery.ConnectionId && g.OrganizationId == delivery.OrganizationId
                && g.ProjectId == delivery.ProjectId && g.RevokedAt == null, timeout.Token);
            if (connection is null)
                throw new ApiException(409, "repository_revoked", "Delivery connection is no longer active.");
            using var payload = JsonDocument.Parse(delivery.Payload);
            long? number = null;
            string? head = null;
            if (payload.RootElement.TryGetProperty("pull_request", out var pr) && pr.TryGetProperty("number", out var num) && num.TryGetInt64(out var parsed)) number = parsed;
            foreach (var type in new[] { "check_run", "check_suite" })
                if (payload.RootElement.TryGetProperty(type, out var check) && check.TryGetProperty("head_sha", out var sha) && sha.ValueKind == JsonValueKind.String) head = sha.GetString();
            var baseChanged = delivery.Event == "push" && payload.RootElement.TryGetProperty("ref", out var reference)
                && reference.GetString() == "refs/heads/" + connection.BaseBranch;
            var ids = await reviews.AffectedAsync(delivery.OrganizationId, delivery.ProjectId, delivery.ConnectionId, number, head, baseChanged, timeout.Token);
            foreach (var id in ids) await reviews.ReconcileAsync(id, null, timeout.Token);
            await db.GitHubDeliveries.Where(d => d.Id == delivery.Id && d.Lease == lease && d.State == "processing")
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.State, "processed").SetProperty(d => d.LeaseUntil, (DateTimeOffset?)null)
                    .SetProperty(d => d.FailureCode, (string?)null), ct);
        }
        catch (Exception error)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var code = error is ApiException api ? api.Code : "webhook_processing_failed";
            await db.GitHubDeliveries.Where(d => d.Id == delivery.Id && d.Lease == lease && d.State == "processing")
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.State, delivery.Attempts < 3 ? "pending" : "failed")
                    .SetProperty(d => d.FailureCode, code).SetProperty(d => d.LeaseUntil, (DateTimeOffset?)null), cleanup.Token);
            throw;
        }
        return true;
    }
}

public sealed class WebhookWorker(IServiceScopeFactory scopes, IConfiguration configuration,
    IHostEnvironment environment, ILogger<WebhookWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (environment.IsEnvironment("Testing")) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (configuration.GetValue<bool>("GitHub:EnableReview"))
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<WebhookProcessor>().ProcessOneAsync(stoppingToken);
                }
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception)
            {
                logger.LogWarning("Webhook processing stopped; will retry with persisted attempt limits");
                try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            }
        }
    }
}
