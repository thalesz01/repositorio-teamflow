using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Executions;
using TeamFlow.Api.Features.Projects;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Runners;

public static class RunnerEndpoints
{
    public static void MapRunners(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/organizations/{organizationId:guid}/projects/{projectId:guid}/runners")
            .RequireAuthorization().WithTags("Runners");
        group.MapPost("/", async (Guid organizationId, Guid projectId, RunnerInput input, ClaimsPrincipal user,
            AppDbContext db, ProjectAccess access, TimeProvider clock, CancellationToken ct) =>
        {
            await access.RequireAsync(organizationId, projectId, user, ct, owner: true);
            var issued = Issue(organizationId, projectId, input, clock);
            db.RunnerCredentials.Add(issued.Entity);
            await db.SaveChangesAsync(ct);
            return Results.Ok(issued.Response);
        }).Produces<IssuedRunner>();
        // Any member may see whether the project can run work; managing credentials stays with the owner.
        group.MapGet("/status", async (Guid organizationId, Guid projectId, ClaimsPrincipal user, AppDbContext db,
            ProjectAccess access, TimeProvider clock, CancellationToken ct) =>
        {
            await access.RequireAsync(organizationId, projectId, user, ct);
            var now = clock.GetUtcNow();
            var active = await db.RunnerCredentials.AsNoTracking().Where(r => r.OrganizationId == organizationId
                && r.ProjectId == projectId && r.RevokedAt == null && r.ExpiresAt > now).Select(r => r.LastSeenAt).ToListAsync(ct);
            var lastSeen = active.Max();
            return Results.Ok(new RunnerStatusDto(active.Count > 0, lastSeen is { } seen && now - seen <= TimeSpan.FromSeconds(60), lastSeen));
        }).Produces<RunnerStatusDto>();
        group.MapGet("/", async (Guid organizationId, Guid projectId, ClaimsPrincipal user, AppDbContext db,
            ProjectAccess access, CancellationToken ct, int page = 1, int pageSize = 20) =>
        {
            await access.RequireAsync(organizationId, projectId, user, ct, owner: true);
            var pagination = Pagination.Validate(page, pageSize);
            var query = db.RunnerCredentials.AsNoTracking().Where(r => r.OrganizationId == organizationId && r.ProjectId == projectId);
            var total = await query.CountAsync(ct);
            var items = await query.OrderBy(r => r.Id).Skip((pagination.Page - 1) * pagination.PageSize).Take(pagination.PageSize)
                .Select(r => new RunnerDto(r.Id, r.WorkerId, r.ExpiresAt, r.RevokedAt)).ToListAsync(ct);
            return Results.Ok(new PageResult<RunnerDto>(items, pagination.Page, pagination.PageSize, total));
        }).Produces<PageResult<RunnerDto>>();
        group.MapPost("/{id:guid}/rotate", async (Guid organizationId, Guid projectId, Guid id, RunnerInput input,
            ClaimsPrincipal user, AppDbContext db, ProjectAccess access, TimeProvider clock, CancellationToken ct) =>
        {
            await access.RequireAsync(organizationId, projectId, user, ct, owner: true);
            var replacement = Issue(organizationId, projectId, input, clock);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var changed = await db.RunnerCredentials.Where(r => r.Id == id && r.OrganizationId == organizationId
                && r.ProjectId == projectId && r.RevokedAt == null)
                .ExecuteUpdateAsync(set => set.SetProperty(r => r.RevokedAt, clock.GetUtcNow()), ct);
            if (changed != 1) throw new ApiException(404, "runner_not_found", "Active runner not found.");
            db.RunnerCredentials.Add(replacement.Entity);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Ok(replacement.Response);
        }).Produces<IssuedRunner>();
        group.MapDelete("/{id:guid}", async (Guid organizationId, Guid projectId, Guid id, ClaimsPrincipal user,
            AppDbContext db, ProjectAccess access, TimeProvider clock, CancellationToken ct) =>
        {
            await access.RequireAsync(organizationId, projectId, user, ct, owner: true);
            var count = await db.RunnerCredentials.Where(r => r.Id == id && r.OrganizationId == organizationId && r.ProjectId == projectId)
                .ExecuteUpdateAsync(set => set.SetProperty(r => r.RevokedAt, clock.GetUtcNow()), ct);
            if (count == 0) throw new ApiException(404, "runner_not_found", "Runner not found.");
            return Results.NoContent();
        });

        var internalGroup = app.MapGroup("/internal/v1/executions").RequireAuthorization("runner").RequireRateLimiting("runner").WithTags("Runner protocol");
        internalGroup.MapPost("/claim", async (ClaimInput input, ClaimsPrincipal user, ExecutionScheduler scheduler, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            ValidateLease(input.LeaseSeconds);
            var runnerId = Id(user, "runnerId");
            // Polling is the runner's heartbeat; writing at most every 15 seconds keeps the table quiet.
            var now = clock.GetUtcNow();
            await db.RunnerCredentials.Where(r => r.Id == runnerId && (r.LastSeenAt == null || r.LastSeenAt < now.AddSeconds(-15)))
                .ExecuteUpdateAsync(set => set.SetProperty(r => r.LastSeenAt, now), ct);
            var lease = await scheduler.ClaimAsync(Id(user, "workerId"), input.LeaseSeconds, ct,
                Id(user, "organizationId"), Id(user, "projectId"), runnerId);
            if (lease is null) return Results.NoContent();
            var execution = await db.Executions.AsNoTracking().SingleAsync(e => e.Id == lease.ExecutionId, ct);
            return Results.Ok(new RunnerMessage(1, execution.Id, execution.TaskId, execution.OrganizationId, execution.ProjectId,
                lease.WorkerId, lease.LeaseToken, lease.LeaseExpiresAt, lease.DeadlineAt, lease.Attempt,
                JsonSerializer.Deserialize<JsonElement>(lease.TaskSnapshot),
                new(execution.MaxDurationSeconds, execution.MaxAttempts, execution.MaxSteps, execution.MaxCostUsd),
                execution.RepositorySnapshot is null ? null : JsonSerializer.Deserialize<JsonElement>(execution.RepositorySnapshot), execution.AgentProfileId,
                execution.MemoryContext is null ? null : JsonSerializer.Deserialize<JsonElement>(execution.MemoryContext),
                execution.TransferSnapshot is null ? null : JsonSerializer.Deserialize<JsonElement>(execution.TransferSnapshot)));
        }).Produces<RunnerMessage>().Produces(204);
        internalGroup.MapPost("/{id:guid}/heartbeat", async (Guid id, HeartbeatInput input, ClaimsPrincipal user,
            ExecutionScheduler scheduler, CancellationToken ct) =>
        {
            ValidateLease(input.LeaseSeconds);
            var renewed = await scheduler.RenewAsync(id, Id(user, "workerId"), input.LeaseToken, input.LeaseSeconds, ct,
                Id(user, "runnerId"));
            return renewed ? Results.Ok(new HeartbeatReply(true))
                : Results.Conflict(new HeartbeatReply(false, "lease_lost"));
        }).Produces<HeartbeatReply>().Produces<HeartbeatReply>(409);
    }

    private static Guid Id(ClaimsPrincipal user, string claim) => Guid.Parse(user.FindFirstValue(claim) ?? throw new InvalidOperationException("Missing runner identity."));
    private static void ValidateLease(int seconds)
    {
        if (seconds is < 5 or > 120) throw new ApiException(400, "invalid_lease", "Lease must be 5..120 seconds.");
    }

    internal static (RunnerCredential Entity, IssuedRunner Response) Issue(Guid organizationId, Guid projectId, RunnerInput input, TimeProvider clock)
    {
        if (input.ValidityDays is < 1 or > 30) throw new ApiException(400, "invalid_runner_expiry", "Validity must be 1..30 days.");
        var id = Guid.NewGuid();
        var token = $"{id:N}.{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";
        var credential = new RunnerCredential
        {
            Id = id,
            OrganizationId = organizationId,
            ProjectId = projectId,
            WorkerId = Guid.NewGuid(),
            TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
            ExpiresAt = clock.GetUtcNow().AddDays(input.ValidityDays)
        };
        return (credential, new(new(id, credential.WorkerId, credential.ExpiresAt, null), token));
    }
}
