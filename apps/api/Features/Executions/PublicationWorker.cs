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
                if (configuration.GetValue<bool>("Local:EnablePublication"))
                {
                    using var scope = scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var now = clock.GetUtcNow();
                    var id = await db.Executions.AsNoTracking().Where(e => e.State == ExecutionState.Succeeded
                        && e.ResultCode == "tests_passed" && e.PublicationState != "published" && e.PublicationAttempts < 3
                        && (e.PublicationLeaseUntil == null || e.PublicationLeaseUntil <= now))
                        .OrderBy(e => e.CreatedAt).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(stoppingToken);
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
