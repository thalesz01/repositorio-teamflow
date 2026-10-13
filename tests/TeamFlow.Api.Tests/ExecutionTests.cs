using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Executions;
using TeamFlow.Api.Features.Projects;
using TeamFlow.Api.Features.Tasks;
using Xunit;

namespace TeamFlow.Api.Tests;

public sealed class ExecutionTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    private static async Task<TaskDto> ReadyAsync(HttpClient client, ProjectDto project, Guid[]? dependencies = null)
    {
        var task = await client.CreateTaskAsync(project, dependencies);
        var path = ApiClient.TasksPath(project) + $"/{task.Id}/transition";
        using var analysis = await client.SendAsync(HttpMethod.Post, path, new TransitionInput(TaskStage.Analysis, task.Version));
        var analyzed = await analysis.ReadAsync<TaskDto>();
        using var ready = await client.SendAsync(HttpMethod.Post, path, new TransitionInput(TaskStage.Ready, analyzed.Version));
        return await ready.ReadAsync<TaskDto>();
    }

    [Fact]
    public async Task SchedulingIsIdempotentAndRejectsChangedPayloadsAsync()
    {
        using var client = fixture.Factory.CreateClient();
        await client.RegisterAsync();
        var project = await client.CreateProjectAsync();
        var task = await ReadyAsync(client, project);
        var path = ApiClient.TasksPath(project) + $"/{task.Id}/executions";
        var input = new ExecutionInput(task.Version);
        using var firstResponse = await client.SendAsync(HttpMethod.Post, path + "/", input, "same-command");
        var first = await firstResponse.ReadAsync<ExecutionDto>();
        using var repeat = await client.SendAsync(HttpMethod.Post, path + "/", input, "same-command");
        Assert.Equal(first.Id, (await repeat.ReadAsync<ExecutionDto>()).Id);
        using var altered = await client.SendAsync(HttpMethod.Post, path + "/", input with { MaxAttempts = 4 }, "same-command");
        Assert.Equal(HttpStatusCode.Conflict, altered.StatusCode);
        using var active = await client.SendAsync(HttpMethod.Post, path + "/", input, "another-command");
        Assert.Equal(HttpStatusCode.Conflict, active.StatusCode);
        using var limit = await client.SendAsync(HttpMethod.Post, path + "/", input with { MaxSteps = 1000 }, "bad-limits");
        Assert.Equal(HttpStatusCode.BadRequest, limit.StatusCode);
        using var fractionalBudget = await client.SendAsync(HttpMethod.Post, path + "/", input with { MaxCostUsd = 0.015m }, "fractional-budget");
        Assert.Equal(HttpStatusCode.BadRequest, fractionalBudget.StatusCode);
        using var cancel = await client.SendAsync(HttpMethod.Post, path + $"/{first.Id}/cancel", new { });
        Assert.Equal(ExecutionState.Cancelled, (await cancel.ReadAsync<ExecutionDto>()).State);
        using var repeatAfterCancel = await client.SendAsync(HttpMethod.Post, path + "/", input, "same-command");
        Assert.Equal(first.Id, (await repeatAfterCancel.ReadAsync<ExecutionDto>()).Id);
    }

    [Fact]
    public async Task ConcurrentCommandsCreateOnlyOneActiveExecutionAsync()
    {
        using var client = fixture.Factory.CreateClient();
        await client.RegisterAsync();
        var project = await client.CreateProjectAsync();
        var task = await ReadyAsync(client, project);
        var path = ApiClient.TasksPath(project) + $"/{task.Id}/executions";
        var input = new ExecutionInput(task.Version);
        var responses = await Task.WhenAll(client.SendAsync(HttpMethod.Post, path + "/", input, "first"), client.SendAsync(HttpMethod.Post, path + "/", input, "second"));
        try
        {
            var accepted = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
            var execution = await accepted.ReadAsync<ExecutionDto>();
            using var cancel = await client.SendAsync(HttpMethod.Post, path + $"/{execution.Id}/cancel", new { });
            Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task RestartAndLeaseRecoveryPreserveWorkAndRejectStaleOwnersAsync()
    {
        using var client = fixture.Factory.CreateClient();
        await client.RegisterAsync();
        var project = await client.CreateProjectAsync();
        var task = await ReadyAsync(client, project);
        var path = ApiClient.TasksPath(project) + $"/{task.Id}/executions/";
        using var schedule = await client.SendAsync(HttpMethod.Post, path, new ExecutionInput(task.Version, MaxAttempts: 2), "restart-command");
        var execution = await schedule.ReadAsync<ExecutionDto>();
        await fixture.RestartAsync();
        var first = await fixture.WithSchedulerAsync(s => s.ClaimAsync(Guid.NewGuid(), 5, CancellationToken.None));
        Assert.NotNull(first);
        Assert.Equal(execution.Id, first.ExecutionId);
        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        var second = await fixture.WithSchedulerAsync(s => s.ClaimAsync(Guid.NewGuid(), 5, CancellationToken.None));
        Assert.NotNull(second);
        Assert.Equal(first.ExecutionId, second.ExecutionId);
        Assert.Equal(first.TaskSnapshot, second.TaskSnapshot);
        Assert.Equal(2, second.Attempt);
        Assert.NotEqual(first.LeaseToken, second.LeaseToken);
        Assert.False(await fixture.WithSchedulerAsync(s => s.RenewAsync(first.ExecutionId, first.WorkerId, first.LeaseToken, 5, CancellationToken.None)));
        Assert.True(await fixture.WithSchedulerAsync(s => s.RenewAsync(second.ExecutionId, second.WorkerId, second.LeaseToken, 5, CancellationToken.None)));
        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        Assert.Null(await fixture.WithSchedulerAsync(s => s.ClaimAsync(Guid.NewGuid(), 5, CancellationToken.None)));
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(ExecutionState.Failed, (await db.Executions.SingleAsync(e => e.Id == execution.Id)).State);
        Assert.Equal(TaskStage.Failed, (await db.WorkItems.SingleAsync(t => t.Id == task.Id)).Stage);
    }

    [Fact]
    public async Task PendingDependenciesAndOtherTeamsCannotScheduleAsync()
    {
        using var client = fixture.Factory.CreateClient();
        using var outsider = fixture.Factory.CreateClient();
        await client.RegisterAsync();
        await outsider.RegisterAsync();
        var project = await client.CreateProjectAsync();
        var dependency = await client.CreateTaskAsync(project);
        var task = await ReadyAsync(client, project, [dependency.Id]);
        var path = ApiClient.TasksPath(project) + $"/{task.Id}/executions/";
        using var blocked = await client.SendAsync(HttpMethod.Post, path, new ExecutionInput(task.Version), "blocked");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        using var foreign = await outsider.SendAsync(HttpMethod.Post, path, new ExecutionInput(task.Version), "foreign");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        var unchanged = await client.GetFromJsonAsync<TaskDto>(ApiClient.TasksPath(project) + $"/{task.Id}", ApiClient.JsonOptions);
        Assert.NotNull(unchanged);
        Assert.Equal(TaskStage.Ready, unchanged.Stage);
    }

    [Fact]
    public async Task CompetingWorkersCannotClaimTheSameLeaseAsync()
    {
        using var client = fixture.Factory.CreateClient();
        await client.RegisterAsync();
        var project = await client.CreateProjectAsync();
        var task = await ReadyAsync(client, project);
        var path = ApiClient.TasksPath(project) + $"/{task.Id}/executions";
        using var response = await client.SendAsync(HttpMethod.Post, path + "/", new ExecutionInput(task.Version), "workers");
        var execution = await response.ReadAsync<ExecutionDto>();
        var leases = await Task.WhenAll(fixture.WithSchedulerAsync(s => s.ClaimAsync(Guid.NewGuid(), 5, CancellationToken.None)),
            fixture.WithSchedulerAsync(s => s.ClaimAsync(Guid.NewGuid(), 5, CancellationToken.None)));
        var claimed = Assert.Single(leases, lease => lease is not null);
        Assert.NotNull(claimed);
        Assert.Equal(execution.Id, claimed.ExecutionId);
        using var cancel = await client.SendAsync(HttpMethod.Post, path + $"/{execution.Id}/cancel", new { });
        Assert.True((await cancel.ReadAsync<ExecutionDto>()).CancellationRequested);
        Assert.False(await fixture.WithSchedulerAsync(s => s.RenewAsync(claimed.ExecutionId, claimed.WorkerId, claimed.LeaseToken, 5, CancellationToken.None)));
        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        Assert.Null(await fixture.WithSchedulerAsync(s => s.ClaimAsync(Guid.NewGuid(), 5, CancellationToken.None)));
    }
}
