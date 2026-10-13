using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Executions;
using TeamFlow.Api.Features.Integrations;
using TeamFlow.Api.Features.Runners;
using TeamFlow.Api.Features.Tasks;
using TeamFlow.Runner;
using Xunit;

namespace TeamFlow.Api.Tests;

public sealed class HarnessIntegrationTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task GitFixtureIsEditedTestedAndCheckpointedThroughAuthenticatedApiAsync()
    {
        using var workspace = new ExecutionWorkspace(Path.GetFullPath("work/harness-repositories"));
        using var controller = new ExecutionWorkspace(Path.GetFullPath("work/harness-controllers"));
        await GitAsync("init", "--template=" + controller.Path);
        await File.WriteAllTextAsync(workspace.Resolve("answer.txt"), "41");
        await GitAsync("add", "--", "answer.txt");
        await GitAsync("-c", "user.name=Fixture", "-c", "user.email=fixture@example.test", "-c", "commit.gpgsign=false",
            "-c", "core.hooksPath=" + controller.Path, "commit", "-m", "Fixture base");
        var commit = (await GitAsync("rev-parse", "HEAD")).Trim();
        using var human = fixture.Factory.CreateClient();
        await human.RegisterAsync();
        var project = await human.CreateProjectAsync();
        var task = await human.CreateTaskAsync(project);
        var path = ApiClient.TasksPath(project) + $"/{task.Id}";
        using var analysis = await human.SendAsync(HttpMethod.Post, path + "/transition", new TransitionInput(TaskStage.Analysis, task.Version));
        task = await analysis.ReadAsync<TaskDto>();
        using var ready = await human.SendAsync(HttpMethod.Post, path + "/transition", new TransitionInput(TaskStage.Ready, task.Version));
        task = await ready.ReadAsync<TaskDto>();
        using var schedule = await human.SendAsync(HttpMethod.Post, path + "/executions/", new ExecutionInput(task.Version), Guid.NewGuid().ToString());
        var execution = await schedule.ReadAsync<ExecutionDto>();
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var source = new RepositorySnapshot(Guid.NewGuid(), Guid.NewGuid(), 1, "https://github.com/fixture/repo.git",
                "main", commit, $"teamflow/{execution.Id:N}");
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Executions.Where(e => e.Id == execution.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.RepositorySnapshot, JsonSerializer.Serialize(source, ApiClient.JsonOptions)));
        }
        using var issue = await human.SendAsync(HttpMethod.Post,
            $"/api/v1/organizations/{project.OrganizationId}/projects/{project.Id}/runners/", new RunnerInput());
        var credential = await issue.ReadAsync<IssuedRunner>();
        using var runner = fixture.Factory.CreateClient();
        runner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential.Token);
        using var claim = await runner.PostAsJsonAsync("/internal/v1/executions/claim", new ClaimInput(120));
        var message = await claim.ReadAsync<JsonElement>();
        var lease = message.GetProperty("leaseToken").GetGuid();
        var checkpointPath = $"/internal/v1/executions/{execution.Id}/checkpoint";
        var context = new PortableContext(1, execution.Id, task.Title + "\n" + task.Description, task.AcceptanceCriteria, commit, [], []);
        var tools = new WorkspaceTools(workspace, new DockerSandbox(), ["answer.txt"], ["answer.txt"],
            new SandboxCommand("/bin/sh", ["-c", "test \"$(cat answer.txt)\" = 42"], TimeSpan.FromSeconds(10)));
        var result = await new AgentHarness(new FixtureModel(), new ApiCheckpoint(runner, checkpointPath, lease), tools)
            .RunAsync(context, 4, TimeSpan.FromSeconds(30), default);
        Assert.True(result.Validated);
        var patch = await GitAsync("diff", "--", "answer.txt");
        Assert.Contains("-41", patch);
        Assert.Contains("+42", patch);
        using var read = await runner.PostAsJsonAsync(checkpointPath + "/read", new CheckpointReadInput(lease));
        var saved = await read.ReadAsync<CheckpointReply>();
        Assert.Equal(3, saved.Sequence);
        Assert.Equal(commit, saved.Context.BaseCommit);
        Assert.Equal("test", saved.Context.Evidence[2].Tool);
        Assert.True(saved.Context.Evidence[2].Succeeded);
        using var finalScope = fixture.Factory.Services.CreateScope();
        var db = finalScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(4, await db.ExecutionCheckpoints.CountAsync(c => c.ExecutionId == execution.Id));
        Assert.Equal(TaskStage.Execution, (await db.WorkItems.SingleAsync(t => t.Id == task.Id)).Stage);

        async Task<string> GitAsync(params string[] arguments)
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = workspace.Path,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            start.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Git fixture could not start.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
            Assert.Equal(0, process.ExitCode);
            await error;
            return await output;
        }
    }

    // Explicit model double. HTTP uses TestServer; PostgreSQL, Git and Docker are real.
    private sealed class FixtureModel : IAgentModel
    {
        public Task<AgentAction> NextAsync(PortableContext context, CancellationToken ct) => Task.FromResult(context.Evidence.Length switch
        {
            0 => new AgentAction("read", "answer.txt"),
            1 => new AgentAction("write", "answer.txt", "42", context.Evidence[0].Hash),
            2 => new AgentAction("test"),
            _ => new AgentAction("finish")
        });
    }
    private sealed class ApiCheckpoint(HttpClient client, string path, Guid lease) : IAgentCheckpoint
    {
        public async Task SaveAsync(PortableContext context, CancellationToken ct)
        {
            using var response = await client.PutAsJsonAsync(path + "/", new { leaseToken = lease, context }, ct);
            response.EnsureSuccessStatusCode();
        }
    }
}
