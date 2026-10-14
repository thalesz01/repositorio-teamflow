using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Integrations;
using TeamFlow.Api.Features.Executions;
using TeamFlow.Api.Features.Tasks;

namespace TeamFlow.Api.Features.Runners;

// Local operator command, never an HTTP endpoint. The normal owner API is unchanged.
public static class LocalRunnerProvisioning
{
    public static async Task<ExecutionDto> RetryAsync(IServiceProvider services, IHostEnvironment environment, Guid executionId, CancellationToken ct)
    {
        if (!environment.IsDevelopment()) throw new InvalidOperationException("Local retry requires Development.");
        var db = services.GetRequiredService<AppDbContext>();
        var previous = await db.Executions.SingleAsync(e => e.Id == executionId && e.State == ExecutionState.Failed, ct);
        var task = await db.WorkItems.AsNoTracking().SingleAsync(t => t.Id == previous.TaskId, ct);
        var repository = await db.GitConnections.AsNoTracking().FirstAsync(r => r.OrganizationId == previous.OrganizationId
            && r.ProjectId == previous.ProjectId && r.Provider == "local" && r.RevokedAt == null
            && (task.RepositoryConnectionId == null ? !r.ForTasks : r.Id == task.RepositoryConnectionId), ct);
        services.GetRequiredService<LocalRepository>().AuthorizedPath(previous.OrganizationId, previous.ProjectId, repository.LocalPath);
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, previous.RequestedBy.ToString())], "local-operator"));
        var tracked = await services.GetRequiredService<TaskAccess>().RequireAsync(previous.OrganizationId, previous.ProjectId, previous.TaskId, user, ct);
        TaskRules.RequireTransition(tracked, TaskStage.Ready);
        task = tracked;
        task.Stage = TaskStage.Ready;
        task.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return await services.GetRequiredService<ExecutionScheduler>().ScheduleAsync(previous.OrganizationId, previous.ProjectId, previous.TaskId,
            new(task.Version, previous.MaxDurationSeconds, previous.MaxAttempts, previous.MaxSteps, previous.MaxCostUsd, previous.AgentProfileId),
            "local-retry-" + executionId.ToString("N"), user, ct);
    }

    public static async Task<IssuedRunner> IssueAsync(IServiceProvider services, IHostEnvironment environment,
        Guid organizationId, Guid projectId, CancellationToken ct)
    {
        if (!environment.IsDevelopment() || organizationId == Guid.Empty || projectId == Guid.Empty)
            throw new InvalidOperationException("Local runner provisioning requires Development and explicit project IDs.");
        var db = services.GetRequiredService<AppDbContext>();
        // Folders arrive with each task, so a runner only needs an existing project. A project that still has its own folder must keep it authorized.
        if (!await db.Projects.AnyAsync(p => p.Id == projectId && p.OrganizationId == organizationId, ct))
            throw new InvalidOperationException("The project does not exist.");
        var repository = await db.GitConnections.AsNoTracking().SingleOrDefaultAsync(r => r.OrganizationId == organizationId
            && r.ProjectId == projectId && r.Provider == "local" && r.RevokedAt == null && !r.ForTasks, ct);
        if (repository is not null) services.GetRequiredService<LocalRepository>().AuthorizedPath(organizationId, projectId, repository.LocalPath);
        var issued = RunnerService.Issue(organizationId, projectId, new RunnerInput(), services.GetRequiredService<TimeProvider>());
        db.RunnerCredentials.Add(issued.Entity);
        await db.SaveChangesAsync(ct);
        return issued.Response;
    }
}
