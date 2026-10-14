using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Executions;
using TeamFlow.Api.Features.Integrations;
using TeamFlow.Api.Features.Tasks;
using Xunit;

namespace TeamFlow.Api.Tests;

// ADR 0015: tasks linked as a sequence run one after the other, each on top of the previous step's published commit.
public sealed class TaskSequenceTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    private static readonly string[] Identity = ["-c", "user.name=Fixture", "-c", "user.email=fixture@example.test"];

    private static async Task<string> RevAsync(string repository, string reference)
    {
        var start = new ProcessStartInfo("git", ["rev-parse", reference]) { WorkingDirectory = repository, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output.Trim();
    }

    [Fact]
    public async Task StepsRunInOrderEachOnTopOfThePublishedResultOfThePreviousOneAsync()
    {
        using var client = fixture.Factory.CreateClient();
        await client.RegisterAsync();
        var project = await client.CreateProjectAsync();
        var root = Path.Combine(Path.GetTempPath(), "teamflow-sequence-" + Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(root, "app");
        var elsewhere = Path.Combine(root, "other");
        Directory.CreateDirectory(repository); Directory.CreateDirectory(elsewhere);
        var configuration = fixture.Factory.Services.GetRequiredService<IConfiguration>();
        try
        {
            foreach (var folder in new[] { repository, elsewhere })
            {
                await LocalIntegrationTests.GitAsync(folder, ["init", "-b", "main"]);
                await LocalIntegrationTests.GitAsync(folder, [.. Identity, "commit", "--allow-empty", "-m", "One"]);
            }
            configuration["Local:BrowseRoots:0"] = root; configuration["Local:AllowRepositorySelection"] = "true";
            var tasks = ApiClient.TasksPath(project) + "/";
            TaskInput Draft(string title, string folder, Guid? previous = null) =>
                new(title, "Do the work.", ["It works."], null, [], FolderPath: folder, Stage: TaskStage.Ready, PreviousTaskId: previous);
            async Task<TaskDto> CreateAsync(TaskInput input) { using var response = await client.SendAsync(HttpMethod.Post, tasks, input); Assert.Equal(HttpStatusCode.Created, response.StatusCode); return await response.ReadAsync<TaskDto>(); }

            var first = await CreateAsync(Draft("Step one", repository));
            var second = await CreateAsync(Draft("Step two", repository, first.Id));
            var third = await CreateAsync(Draft("Step three", repository, second.Id));
            Assert.Equal(first.Id, second.PreviousTaskId);

            using var fork = await client.SendAsync(HttpMethod.Post, tasks, Draft("Fork", repository, first.Id));
            Assert.Equal(HttpStatusCode.Conflict, fork.StatusCode);
            using var otherFolder = await client.SendAsync(HttpMethod.Post, tasks, Draft("Elsewhere", elsewhere, third.Id));
            Assert.Equal(HttpStatusCode.BadRequest, otherFolder.StatusCode);
            using var unknown = await client.SendAsync(HttpMethod.Post, tasks, Draft("Ghost", repository, Guid.NewGuid()));
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
            var single = await CreateAsync(Draft("Alone", elsewhere));
            using var alone = await client.SendAsync(HttpMethod.Post, $"{tasks}{single.Id}/sequence/start", new { });
            Assert.Equal(HttpStatusCode.Conflict, alone.StatusCode);

            using var solo = await client.SendAsync(HttpMethod.Post, $"{tasks}{second.Id}/executions/", new ExecutionInput(second.Version), Guid.NewGuid().ToString());
            Assert.Equal(HttpStatusCode.Conflict, solo.StatusCode);

            // Starting any step marks the waiting ones and schedules only the first.
            using var started = await client.SendAsync(HttpMethod.Post, $"{tasks}{second.Id}/sequence/start", new { });
            var chain = await started.ReadAsync<TaskDto[]>();
            Assert.Equal(["Step one", "Step two", "Step three"], chain.Select(t => t.Title));
            Assert.Equal(TaskStage.Execution, chain[0].Stage);
            Assert.All(chain.Skip(1), step => { Assert.Equal(TaskStage.Ready, step.Stage); Assert.True(step.SequenceRunning); });

            using var scope = fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sequences = scope.ServiceProvider.GetRequiredService<TaskSequences>();
            Assert.Equal(0, await sequences.AdvanceAsync(null, default));
            Assert.Empty(await db.Executions.Where(e => e.TaskId == second.Id).ToListAsync());

            // The first step publishes: the second one starts from that commit, not from the branch tip.
            var firstRun = await db.Executions.AsNoTracking().SingleAsync(e => e.TaskId == first.Id);
            var tip = await RevAsync(repository, "main");
            await LocalIntegrationTests.GitAsync(repository, ["checkout", "-q", "-b", "scratch"]);
            File.WriteAllText(Path.Combine(repository, "one.txt"), "first step");
            await LocalIntegrationTests.GitAsync(repository, ["add", "one.txt"]);
            await LocalIntegrationTests.GitAsync(repository, [.. Identity, "commit", "-q", "-m", "step one result"]);
            var head = await RevAsync(repository, "HEAD");
            await LocalIntegrationTests.GitAsync(repository, ["checkout", "-q", "main"]);
            await LocalIntegrationTests.GitAsync(repository, ["branch", "-f", $"teamflow/{firstRun.Id:N}", head]);
            await db.Executions.Where(e => e.Id == firstRun.Id).ExecuteUpdateAsync(set => set.SetProperty(e => e.State, ExecutionState.Succeeded)
                .SetProperty(e => e.PublicationState, "published").SetProperty(e => e.PublishedHeadCommit, head));
            await db.WorkItems.Where(t => t.Id == first.Id).ExecuteUpdateAsync(set => set.SetProperty(t => t.Stage, TaskStage.Review));

            Assert.Equal(1, await sequences.AdvanceAsync(null, default));
            Assert.Equal(1, await db.Executions.CountAsync(e => e.TaskId == second.Id));
            Assert.Equal(0, await db.Executions.CountAsync(e => e.TaskId == third.Id));
            var secondRun = await db.Executions.AsNoTracking().SingleAsync(e => e.TaskId == second.Id);
            var snapshot = JsonSerializer.Deserialize<RepositorySnapshot>(secondRun.RepositorySnapshot!, ApiClient.JsonOptions)!;
            Assert.Equal(head, snapshot.BaseCommit);
            Assert.Equal(firstRun.Id, snapshot.StackedOn);
            Assert.Equal(tip, await RevAsync(repository, "main"));

            // Correcting the first step now would orphan the second one.
            using var correction = await client.SendAsync(HttpMethod.Post, $"{tasks}{first.Id}/executions/", new ExecutionInput(first.Version, ParentExecutionId: firstRun.Id, Feedback: "Change it."), Guid.NewGuid().ToString());
            Assert.Equal(HttpStatusCode.Conflict, correction.StatusCode);

            // A failed step stops the sequence instead of running the next one on nothing.
            await db.Executions.Where(e => e.Id == secondRun.Id).ExecuteUpdateAsync(set => set.SetProperty(e => e.State, ExecutionState.Failed));
            await db.WorkItems.Where(t => t.Id == second.Id).ExecuteUpdateAsync(set => set.SetProperty(t => t.Stage, TaskStage.Failed));
            Assert.Equal(0, await sequences.AdvanceAsync(null, default));
            Assert.Equal(0, await db.Executions.CountAsync(e => e.TaskId == third.Id));
            Assert.Null((await db.WorkItems.AsNoTracking().SingleAsync(t => t.Id == third.Id)).SequenceStartedBy);
        }
        finally
        {
            configuration["Local:AllowRepositorySelection"] = "false";
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }
}
