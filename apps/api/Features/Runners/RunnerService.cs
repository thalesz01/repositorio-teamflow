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

public sealed class RunnerService(AppDbContext db, ProjectAccess projects, ExecutionScheduler scheduler, TimeProvider clock)
{
    public async Task<IssuedRunner> IssueAsync(Guid organizationId, Guid projectId, RunnerInput input, ClaimsPrincipal user, CancellationToken ct)
    {
        await projects.RequireAsync(organizationId, projectId, user, ct, owner: true);
        var issued = Issue(organizationId, projectId, input, clock);
        db.RunnerCredentials.Add(issued.Entity);
        await db.SaveChangesAsync(ct);
        return issued.Response;
    }

    // Any member may see whether the project can run work; managing credentials stays with the owner.
    public async Task<RunnerStatusDto> StatusAsync(Guid organizationId, Guid projectId, ClaimsPrincipal user, CancellationToken ct)
    {
        await projects.RequireAsync(organizationId, projectId, user, ct);
        var now = clock.GetUtcNow();
        var active = await db.RunnerCredentials.AsNoTracking().Where(r => r.OrganizationId == organizationId
            && r.ProjectId == projectId && r.RevokedAt == null && r.ExpiresAt > now).Select(r => r.LastSeenAt).ToListAsync(ct);
        var lastSeen = active.Max();
        return new RunnerStatusDto(active.Count > 0, lastSeen is { } seen && now - seen <= TimeSpan.FromSeconds(60), lastSeen);
    }

    public async Task<PageResult<RunnerDto>> ListAsync(Guid organizationId, Guid projectId, int page, int pageSize, ClaimsPrincipal user, CancellationToken ct)
    {
        await projects.RequireAsync(organizationId, projectId, user, ct, owner: true);
        var pagination = Pagination.Validate(page, pageSize);
        var query = db.RunnerCredentials.AsNoTracking().Where(r => r.OrganizationId == organizationId && r.ProjectId == projectId);
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(r => r.Id).Skip((pagination.Page - 1) * pagination.PageSize).Take(pagination.PageSize)
            .Select(r => new RunnerDto(r.Id, r.WorkerId, r.ExpiresAt, r.RevokedAt)).ToListAsync(ct);
        return new PageResult<RunnerDto>(items, pagination.Page, pagination.PageSize, total);
    }

    public async Task<IssuedRunner> RotateAsync(Guid organizationId, Guid projectId, Guid id, RunnerInput input, ClaimsPrincipal user, CancellationToken ct)
    {
        await projects.RequireAsync(organizationId, projectId, user, ct, owner: true);
        var replacement = Issue(organizationId, projectId, input, clock);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var changed = await db.RunnerCredentials.Where(r => r.Id == id && r.OrganizationId == organizationId
            && r.ProjectId == projectId && r.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.RevokedAt, clock.GetUtcNow()), ct);
        if (changed != 1) throw new ApiException(404, "runner_not_found", "Active runner not found.");
        db.RunnerCredentials.Add(replacement.Entity);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return replacement.Response;
    }

    public async Task RevokeAsync(Guid organizationId, Guid projectId, Guid id, ClaimsPrincipal user, CancellationToken ct)
    {
        await projects.RequireAsync(organizationId, projectId, user, ct, owner: true);
        var count = await db.RunnerCredentials.Where(r => r.Id == id && r.OrganizationId == organizationId && r.ProjectId == projectId)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.RevokedAt, clock.GetUtcNow()), ct);
        if (count == 0) throw new ApiException(404, "runner_not_found", "Runner not found.");
    }

    public async Task<RunnerMessage?> ClaimAsync(ClaimInput input, ClaimsPrincipal runner, CancellationToken ct)
    {
        ValidateLease(input.LeaseSeconds);
        var runnerId = Id(runner, "runnerId");
        // Polling is the runner's heartbeat; writing at most every 15 seconds keeps the table quiet.
        var now = clock.GetUtcNow();
        await db.RunnerCredentials.Where(r => r.Id == runnerId && (r.LastSeenAt == null || r.LastSeenAt < now.AddSeconds(-15)))
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.LastSeenAt, now), ct);
        var lease = await scheduler.ClaimAsync(Id(runner, "workerId"), input.LeaseSeconds, ct,
            Id(runner, "organizationId"), Id(runner, "projectId"), runnerId);
        if (lease is null) return null;
        var execution = await db.Executions.AsNoTracking().SingleAsync(e => e.Id == lease.ExecutionId, ct);
        return new RunnerMessage(1, execution.Id, execution.TaskId, execution.OrganizationId, execution.ProjectId,
            lease.WorkerId, lease.LeaseToken, lease.LeaseExpiresAt, lease.DeadlineAt, lease.Attempt,
            JsonSerializer.Deserialize<JsonElement>(lease.TaskSnapshot),
            new(execution.MaxDurationSeconds, execution.MaxAttempts, execution.MaxSteps, execution.MaxCostUsd),
            execution.RepositorySnapshot is null ? null : JsonSerializer.Deserialize<JsonElement>(execution.RepositorySnapshot), execution.AgentProfileId,
            execution.MemoryContext is null ? null : JsonSerializer.Deserialize<JsonElement>(execution.MemoryContext),
            execution.TransferSnapshot is null ? null : JsonSerializer.Deserialize<JsonElement>(execution.TransferSnapshot));
    }

    public async Task<HeartbeatReply> HeartbeatAsync(Guid executionId, HeartbeatInput input, ClaimsPrincipal runner, CancellationToken ct)
    {
        ValidateLease(input.LeaseSeconds);
        var renewed = await scheduler.RenewAsync(executionId, Id(runner, "workerId"), input.LeaseToken, input.LeaseSeconds, ct, Id(runner, "runnerId"));
        return renewed ? new HeartbeatReply(true) : new HeartbeatReply(false, "lease_lost");
    }

    public static (RunnerCredential Entity, IssuedRunner Response) Issue(Guid organizationId, Guid projectId, RunnerInput input, TimeProvider clock)
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

    private static Guid Id(ClaimsPrincipal runner, string claim) => Guid.Parse(runner.FindFirstValue(claim) ?? throw new InvalidOperationException("Missing runner identity."));

    private static void ValidateLease(int seconds)
    {
        if (seconds is < 5 or > 120) throw new ApiException(400, "invalid_lease", "Lease must be 5..120 seconds.");
    }
}
