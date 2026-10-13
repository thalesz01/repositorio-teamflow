using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Executions;

public sealed class PublicationWorker(IServiceScopeFactory scopes, IConfiguration configuration,
    IHostEnvironment environment, TimeProvider clock, ILogger<PublicationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (environment.IsEnvironment("Testing")) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (configuration.GetValue<bool>("GitHub:EnablePublication") || configuration.GetValue<bool>("Local:EnablePublication"))
                {
                    using var scope = scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var now = clock.GetUtcNow();
                    var candidates = await db.Executions.AsNoTracking().Where(e => e.State == ExecutionState.Succeeded
                        && e.ResultCode == "tests_passed" && e.PublicationState != "published" && e.PublicationAttempts < 3
                        && (e.PublicationLeaseUntil == null || e.PublicationLeaseUntil <= now))
                                                .OrderBy(e => e.CreatedAt).Select(e => new { e.Id, e.RepositorySnapshot }).Take(100).ToListAsync(stoppingToken);
                    var id = candidates.Where(e =>
                    {
                        var source = e.RepositorySnapshot is null ? null : System.Text.Json.JsonSerializer.Deserialize<TeamFlow.Api.Features.Integrations.RepositorySnapshot>(e.RepositorySnapshot, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
                        return configuration.GetValue<bool>(source?.LocalPath is not null ? "Local:EnablePublication" : "GitHub:EnablePublication");
                    }).Select(e => (Guid?)e.Id).FirstOrDefault();
                    if (id is { } executionId)
                    {
                        try { await scope.ServiceProvider.GetRequiredService<PublicationAccess>().PublishAsync(executionId, stoppingToken); }
                        catch (ApiException error) { logger.LogWarning("Publication {ExecutionId} stopped: {Code}", executionId, error.Code); }
                        catch (Exception) when (!stoppingToken.IsCancellationRequested)
                        { logger.LogWarning("Publication {ExecutionId} stopped with an unexpected failure", executionId); }
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Publication polling unavailable; retrying in 30 seconds");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }
}
