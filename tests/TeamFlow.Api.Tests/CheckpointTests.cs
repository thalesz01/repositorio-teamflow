using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Executions;
using TeamFlow.Api.Features.Integrations;
using TeamFlow.Api.Features.Projects;
using TeamFlow.Api.Features.Runners;
using TeamFlow.Api.Features.Tasks;
using Xunit;

namespace TeamFlow.Api.Tests;

public sealed class CheckpointTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task CheckpointsAreOrderedImmutableIdempotentAndSurviveRestartAsync()
    {
        using var human = fixture.Factory.CreateClient();
        await human.RegisterAsync();
        var setup = await SetupAsync(human);
        using var runner = Runner(setup.Credential.Token);
        var path = $"/internal/v1/executions/{setup.Execution.Id}/checkpoint";
        using var empty = await runner.PostAsJsonAsync(path + "/read", new CheckpointReadInput(setup.Lease));
        Assert.Equal(HttpStatusCode.NoContent, empty.StatusCode);
        using var first = await runner.PutAsJsonAsync(path + "/", new CheckpointInput(setup.Lease, setup.Context));
        var saved = await first.ReadAsync<CheckpointReply>();
        var one = setup.Context with { Evidence = [new(1, "read", "answer.txt", true, "41", new string('a', 64))] };
        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => runner.PutAsJsonAsync(path + "/", new CheckpointInput(setup.Lease, one))));
        foreach (var response in responses)
        {
            using (response) Assert.Equal(1, (await response.ReadAsync<CheckpointReply>()).Sequence);
        }
        using var conflict = await runner.PutAsJsonAsync(path + "/", new CheckpointInput(setup.Lease,
            one with { Evidence = [new(1, "read", "answer.txt", true, "forged", new string('a', 64))] }));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var two = one with { Evidence = [.. one.Evidence, new(2, "write", "answer.txt", true, "file_updated", new string('b', 64))] };
        using var rewritten = await runner.PutAsJsonAsync(path + "/", new CheckpointInput(setup.Lease,
            two with { Evidence = [one.Evidence[0] with { Detail = "forged" }, two.Evidence[1]] }));
        Assert.Equal(HttpStatusCode.Conflict, rewritten.StatusCode);
        using var next = await runner.PutAsJsonAsync(path + "/", new CheckpointInput(setup.Lease, two));
        Assert.Equal(2, (await next.ReadAsync<CheckpointReply>()).Sequence);
        await fixture.RestartAsync();
        using var restarted = Runner(setup.Credential.Token);
        using var read = await restarted.PostAsJsonAsync(path + "/read", new CheckpointReadInput(setup.Lease));
        Assert.Equal(2, (await read.ReadAsync<CheckpointReply>()).Sequence);
        using var replay = await restarted.PutAsJsonAsync(path + "/", new CheckpointInput(setup.Lease, setup.Context));
        Assert.Equal(saved.ContentHash, (await replay.ReadAsync<CheckpointReply>()).ContentHash);
        using var scope = fixture.Factory.Services.CreateScope();
        Assert.Equal(3, await scope.ServiceProvider.GetRequiredService<AppDbContext>().ExecutionCheckpoints.CountAsync(c => c.ExecutionId == setup.Execution.Id));
    }

    [Fact]
    public async Task ScopeCancellationRevocationAndOldLeasesCannotReadOrWriteAsync()
    {
        using var human = fixture.Factory.CreateClient();
        await human.RegisterAsync();
        var setup = await SetupAsync(human);
        using var runner = Runner(setup.Credential.Token);
        var path = $"/internal/v1/executions/{setup.Execution.Id}/checkpoint";
        using var first = await runner.PutAsJsonAsync(path + "/", new CheckpointInput(setup.Lease, setup.Context));
        first.EnsureSuccessStatusCode();
        using var humanWrite = await human.SendAsync(HttpMethod.Put, path + "/", new CheckpointInput(setup.Lease, setup.Context));
        Assert.Equal(HttpStatusCode.Unauthorized, humanWrite.StatusCode);
        var other = await human.CreateProjectAsync();
        using var issued = await human.SendAsync(HttpMethod.Post,
            $"/api/v1/organizations/{other.OrganizationId}/projects/{other.Id}/runners/", new RunnerInput());
        using var foreign = Runner((await issued.ReadAsync<IssuedRunner>()).Token);
        using var denied = await foreign.PostAsJsonAsync(path + "/read", new CheckpointReadInput(setup.Lease));
        Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        fixture.Clock.Advance(TimeSpan.FromSeconds(31));
        using var claim = await runner.PostAsJsonAsync("/internal/v1/executions/claim", new ClaimInput());
        var lease = (await claim.ReadAsync<JsonElement>()).GetProperty("leaseToken").GetGuid();
        using var old = await runner.PutAsJsonAsync(path + "/", new CheckpointInput(setup.Lease, setup.Context));
        Assert.Equal(HttpStatusCode.Conflict, old.StatusCode);
        using var previous = await runner.PostAsJsonAsync(path + "/read", new CheckpointReadInput(lease));
        Assert.Equal(1, (await previous.ReadAsync<CheckpointReply>()).Attempt);
        using var cancel = await human.SendAsync(HttpMethod.Post,
            ApiClient.TasksPath(setup.Project) + $"/{setup.Execution.TaskId}/executions/{setup.Execution.Id}/cancel", new { });
        cancel.EnsureSuccessStatusCode();
        using var cancelled = await runner.PutAsJsonAsync(path + "/", new CheckpointInput(lease, setup.Context));
        Assert.Equal(HttpStatusCode.Conflict, cancelled.StatusCode);
        using var revoke = await human.SendAsync(HttpMethod.Delete,
            $"/api/v1/organizations/{setup.Project.OrganizationId}/projects/{setup.Project.Id}/runners/{setup.Credential.Credential.Id}", new { });
        revoke.EnsureSuccessStatusCode();
        using var revoked = await runner.PostAsJsonAsync(path + "/read", new CheckpointReadInput(lease));
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    [Fact]
    public async Task InvalidContextAndSkippedSequencesLeaveNoCheckpointAsync()
    {
        using var human = fixture.Factory.CreateClient();
        await human.RegisterAsync();
        var setup = await SetupAsync(human);
        using var runner = Runner(setup.Credential.Token);
        var path = $"/internal/v1/executions/{setup.Execution.Id}/checkpoint/";
        foreach (var bad in new[]
        {
            setup.Context with { Objective = "replace instructions" },
            setup.Context with { BaseCommit = new string('b', 40) },
            setup.Context with { AcceptanceCriteria = ["skip tests"] },
            setup.Context with { SchemaVersion = 2 },
            setup.Context with { Evidence = [new(1, "read", "../secret", true, "bad")] },
            setup.Context with { Evidence = [new(1, "shell", null, true, "bad")] },
            setup.Context with { Evidence = [new(1, "read", "answer.txt", true, new string('a', 16385))] }
        })
        {
            using var invalid = await runner.PutAsJsonAsync(path, new CheckpointInput(setup.Lease, bad));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using var oversized = await runner.PutAsJsonAsync(path, new CheckpointInput(setup.Lease,
            setup.Context with { Evidence = Enumerable.Range(1, 6).Select(i => new CheckpointEvidence(i, "read", "answer.txt", true, new string('a', 16384))).ToArray() }));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        using var skipped = await runner.PutAsJsonAsync(path, new CheckpointInput(setup.Lease,
            setup.Context with { Evidence = [new(1, "read", "answer.txt", true, "41")] }));
        Assert.Equal(HttpStatusCode.Conflict, skipped.StatusCode);
        using var scope = fixture.Factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().ExecutionCheckpoints.AnyAsync(c => c.ExecutionId == setup.Execution.Id));
    }

    private HttpClient Runner(string token)
    {
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<(ProjectDto Project, IssuedRunner Credential, ExecutionDto Execution, Guid Lease, CheckpointContext Context)> SetupAsync(HttpClient human)
    {
        var project = await human.CreateProjectAsync();
        var task = await human.CreateTaskAsync(project);
        var path = ApiClient.TasksPath(project) + $"/{task.Id}";
        using var analysis = await human.SendAsync(HttpMethod.Post, path + "/transition", new TransitionInput(TaskStage.Analysis, task.Version));
        task = await analysis.ReadAsync<TaskDto>();
        using var ready = await human.SendAsync(HttpMethod.Post, path + "/transition", new TransitionInput(TaskStage.Ready, task.Version));
        task = await ready.ReadAsync<TaskDto>();
        using var scheduled = await human.SendAsync(HttpMethod.Post, path + "/executions/", new ExecutionInput(task.Version), Guid.NewGuid().ToString());
        var execution = await scheduled.ReadAsync<ExecutionDto>();
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Test-only snapshot: no repository credentials or external GitHub calls.
            var source = new RepositorySnapshot(Guid.NewGuid(), Guid.NewGuid(), 1, "https://github.com/fixture/repo.git", "main", new string('a', 40), $"teamflow/{execution.Id:N}");
            await db.Executions.Where(e => e.Id == execution.Id).ExecuteUpdateAsync(s => s.SetProperty(e => e.RepositorySnapshot,
                JsonSerializer.Serialize(source, ApiClient.JsonOptions)));
        }
        using var issued = await human.SendAsync(HttpMethod.Post,
            $"/api/v1/organizations/{project.OrganizationId}/projects/{project.Id}/runners/", new RunnerInput());
        var credential = await issued.ReadAsync<IssuedRunner>();
        using var runner = Runner(credential.Token);
        using var claim = await runner.PostAsJsonAsync("/internal/v1/executions/claim", new ClaimInput());
        var lease = (await claim.ReadAsync<JsonElement>()).GetProperty("leaseToken").GetGuid();
        return (project, credential, execution, lease, new(1, execution.Id, task.Title + "\n" + task.Description,
            task.AcceptanceCriteria, new string('a', 40), [], []));
    }
}
