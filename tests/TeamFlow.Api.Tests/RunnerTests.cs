using Microsoft.AspNetCore.Hosting;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Executions;
using TeamFlow.Api.Features.Runners;
using TeamFlow.Api.Features.Tasks;
using Xunit;

namespace TeamFlow.Api.Tests;

public sealed class RunnerTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task StatusSeparatesNoRunnerFromAnIdleRunnerAndAnOnlineOneAsync()
    {
        using var human = fixture.Factory.CreateClient();
        using var outsider = fixture.Factory.CreateClient();
        await human.RegisterAsync();
        await outsider.RegisterAsync();
        var project = await human.CreateProjectAsync();
        var runners = $"/api/v1/organizations/{project.OrganizationId}/projects/{project.Id}/runners";
        var none = await human.GetFromJsonAsync<RunnerStatusDto>(runners + "/status", ApiClient.JsonOptions);
        Assert.Equal(new RunnerStatusDto(false, false, null), none);
        using var issue = await human.SendAsync(HttpMethod.Post, runners + "/", new RunnerInput());
        var issued = await issue.ReadAsync<IssuedRunner>();
        var configured = await human.GetFromJsonAsync<RunnerStatusDto>(runners + "/status", ApiClient.JsonOptions);
        Assert.True(configured!.Configured); Assert.False(configured.Online); Assert.Null(configured.LastSeenAt);
        using var technical = fixture.Factory.CreateClient();
        technical.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", issued.Token);
        using var claim = await technical.PostAsJsonAsync("/internal/v1/executions/claim", new ClaimInput());
        Assert.Equal(HttpStatusCode.NoContent, claim.StatusCode);
        var online = await human.GetFromJsonAsync<RunnerStatusDto>(runners + "/status", ApiClient.JsonOptions);
        Assert.True(online!.Online); Assert.NotNull(online.LastSeenAt);
        fixture.Clock.Advance(TimeSpan.FromSeconds(90));
        Assert.False((await human.GetFromJsonAsync<RunnerStatusDto>(runners + "/status", ApiClient.JsonOptions))!.Online);
        using var denied = await outsider.GetAsync(runners + "/status");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
    }

    [Fact]
    public async Task TechnicalIdentityIsScopedRevocableAndSeparateFromHumanSessionsAsync()
    {
        using var human = fixture.Factory.CreateClient();
        await human.RegisterAsync();
        var own = await human.CreateProjectAsync();
        var other = await human.CreateProjectAsync();
        var runners = $"/api/v1/organizations/{own.OrganizationId}/projects/{own.Id}/runners";
        using var issue = await human.SendAsync(HttpMethod.Post, runners + "/", new RunnerInput());
        var issued = await issue.ReadAsync<IssuedRunner>();
        using var technical = fixture.Factory.CreateClient();
        technical.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", issued.Token);
        using var humanClaim = await human.PostAsJsonAsync("/internal/v1/executions/claim", new ClaimInput());
        Assert.Equal(HttpStatusCode.Unauthorized, humanClaim.StatusCode);
        using var technicalSession = await technical.GetAsync("/api/v1/auth/session");
        Assert.Equal(HttpStatusCode.Unauthorized, technicalSession.StatusCode);

        async Task<ExecutionDto> ScheduleAsync(TeamFlow.Api.Features.Projects.ProjectDto project)
        {
            var task = await human.CreateTaskAsync(project);
            var path = ApiClient.TasksPath(project) + $"/{task.Id}";
            using var analysis = await human.SendAsync(HttpMethod.Post, path + "/transition", new TransitionInput(TaskStage.Analysis, task.Version));
            task = await analysis.ReadAsync<TaskDto>();
            using var ready = await human.SendAsync(HttpMethod.Post, path + "/transition", new TransitionInput(TaskStage.Ready, task.Version));
            task = await ready.ReadAsync<TaskDto>();
            using var execution = await human.SendAsync(HttpMethod.Post, path + "/executions/", new ExecutionInput(task.Version), Guid.NewGuid().ToString());
            return await execution.ReadAsync<ExecutionDto>();
        }
        var foreign = await ScheduleAsync(other);
        var expected = await ScheduleAsync(own);
        using var claim = await technical.PostAsJsonAsync("/internal/v1/executions/claim", new
        {
            leaseSeconds = 30,
            organizationId = other.OrganizationId,
            projectId = other.Id,
            workerId = Guid.NewGuid()
        });
        var message = await claim.ReadAsync<JsonElement>();
        Assert.Equal(expected.Id, message.GetProperty("executionId").GetGuid());
        Assert.Equal(own.Id, message.GetProperty("projectId").GetGuid());
        Assert.Equal(900, message.GetProperty("limits").GetProperty("maxDurationSeconds").GetInt32());
        var token = message.GetProperty("leaseToken").GetGuid();
        using var secondClaim = await technical.PostAsJsonAsync("/internal/v1/executions/claim", new ClaimInput());
        Assert.Equal(HttpStatusCode.NoContent, secondClaim.StatusCode);
        using var wrong = await technical.PostAsJsonAsync($"/internal/v1/executions/{expected.Id}/heartbeat", new HeartbeatInput(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Conflict, wrong.StatusCode);
        using var renewed = await technical.PostAsJsonAsync($"/internal/v1/executions/{expected.Id}/heartbeat", new HeartbeatInput(token));
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
        using var cancel = await human.SendAsync(HttpMethod.Post, ApiClient.TasksPath(own) + $"/{expected.TaskId}/executions/{expected.Id}/cancel", new { });
        Assert.True((await cancel.ReadAsync<ExecutionDto>()).CancellationRequested);
        using var cancelledHeartbeat = await technical.PostAsJsonAsync($"/internal/v1/executions/{expected.Id}/heartbeat", new HeartbeatInput(token));
        Assert.Equal(HttpStatusCode.Conflict, cancelledHeartbeat.StatusCode);
        using var rotate = await human.SendAsync(HttpMethod.Post, runners + $"/{issued.Credential.Id}/rotate", new RunnerInput());
        var replacement = await rotate.ReadAsync<IssuedRunner>();
        using var revoked = await technical.PostAsJsonAsync($"/internal/v1/executions/{expected.Id}/heartbeat", new HeartbeatInput(token));
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        technical.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", replacement.Token);
        using var stolen = await technical.PostAsJsonAsync($"/internal/v1/executions/{expected.Id}/heartbeat", new HeartbeatInput(token));
        Assert.Equal(HttpStatusCode.Conflict, stolen.StatusCode);
        using var listing = await human.GetAsync(runners + "/");
        var listingText = await listing.Content.ReadAsStringAsync();
        Assert.DoesNotContain(issued.Token, listingText);
        Assert.DoesNotContain("tokenHash", listingText);
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(ExecutionState.Queued, (await db.Executions.SingleAsync(e => e.Id == foreign.Id)).State);
        Assert.NotEqual(issued.Token, (await db.RunnerCredentials.SingleAsync(r => r.Id == issued.Credential.Id)).TokenHash);
    }

    [Fact]
    public async Task IssuanceRequiresOwnershipAndExpiredCredentialsFailAsync()
    {
        using var owner = fixture.Factory.CreateClient();
        using var outsider = fixture.Factory.CreateClient();
        await owner.RegisterAsync();
        await outsider.RegisterAsync();
        var project = await owner.CreateProjectAsync();
        var path = $"/api/v1/organizations/{project.OrganizationId}/projects/{project.Id}/runners/";
        using var denied = await outsider.SendAsync(HttpMethod.Post, path, new RunnerInput());
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var issue = await owner.SendAsync(HttpMethod.Post, path, new RunnerInput(1));
        var issued = await issue.ReadAsync<IssuedRunner>();
        using var remoteFactory = fixture.Factory.WithWebHostBuilder(builder => builder.UseSetting("AllowedHosts", "*"));
        using var remote = remoteFactory.CreateClient(new() { BaseAddress = new Uri("http://example.test") });
        remote.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", issued.Token);
        using var insecure = await remote.PostAsJsonAsync("/internal/v1/executions/claim", new ClaimInput());
        Assert.Equal(HttpStatusCode.Unauthorized, insecure.StatusCode);
        fixture.Clock.Advance(TimeSpan.FromDays(2));
        using var technical = fixture.Factory.CreateClient();
        technical.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", issued.Token);
        using var expired = await technical.PostAsJsonAsync("/internal/v1/executions/claim", new ClaimInput());
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }
}
