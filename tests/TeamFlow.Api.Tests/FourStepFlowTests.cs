using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Executions;
using TeamFlow.Api.Features.Integrations;
using TeamFlow.Api.Features.Projects;
using TeamFlow.Api.Features.Tasks;
using TeamFlow.Api.Features.Workspace;
using Xunit;

namespace TeamFlow.Api.Tests;

// ADR 0010: personal workspace, folder chosen while creating the task, and tasks that start ready.
public sealed class FourStepFlowTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task WorkspaceIsCreatedOnceReusedAndPrefersExistingWorkAsync()
    {
        using var newcomer = fixture.Factory.CreateClient();
        using var other = fixture.Factory.CreateClient();
        await newcomer.RegisterAsync(); await other.RegisterAsync();
        using var first = await newcomer.SendAsync(HttpMethod.Post, "/api/v1/workspace", new { });
        var workspace = await first.ReadAsync<WorkspaceDto>();
        using var again = await newcomer.SendAsync(HttpMethod.Post, "/api/v1/workspace", new { });
        Assert.Equal(workspace, await again.ReadAsync<WorkspaceDto>());
        using var project = await newcomer.GetAsync($"/api/v1/organizations/{workspace.OrganizationId}/projects/{workspace.ProjectId}");
        Assert.Equal("Tarefas", (await project.ReadAsync<ProjectDto>()).Name);
        using var theirs = await other.SendAsync(HttpMethod.Post, "/api/v1/workspace", new { });
        Assert.NotEqual(workspace.ProjectId, (await theirs.ReadAsync<WorkspaceDto>()).ProjectId);
        using var denied = await other.GetAsync($"/api/v1/organizations/{workspace.OrganizationId}/projects/{workspace.ProjectId}");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

        using var veteran = fixture.Factory.CreateClient();
        await veteran.RegisterAsync();
        var existing = await veteran.CreateProjectAsync();
        await veteran.CreateTaskAsync(existing);
        using var resolved = await veteran.SendAsync(HttpMethod.Post, "/api/v1/workspace", new { });
        Assert.Equal(new WorkspaceDto(existing.OrganizationId, existing.Id), await resolved.ReadAsync<WorkspaceDto>());
    }

    [Fact]
    public async Task TaskKeepsItsFolderStartsReadyAndEachExecutionStartsFromTheCurrentBranchAsync()
    {
        using var client = fixture.Factory.CreateClient();
        await client.RegisterAsync();
        var project = await client.CreateProjectAsync();
        var root = Path.Combine(Path.GetTempPath(), "teamflow-task-folder-" + Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(root, "app");
        Directory.CreateDirectory(repository);
        var configuration = fixture.Factory.Services.GetRequiredService<IConfiguration>();
        try
        {
            await LocalIntegrationTests.GitAsync(repository, ["init", "-b", "main"]);
            await LocalIntegrationTests.GitAsync(repository, ["-c", "user.name=Fixture", "-c", "user.email=fixture@example.test", "commit", "--allow-empty", "-m", "One"]);
            // No per-project section: the server-wide Local section decides which folders can be chosen.
            configuration["Local:BrowseRoots:0"] = root;
            configuration["Local:AllowRepositorySelection"] = "true";
            var tasks = ApiClient.TasksPath(project) + "/";
            TaskInput Draft(string title, string? folder, TaskStage? stage, string[]? criteria = null, string? branch = null) =>
                new(title, "Do the work.", criteria ?? ["It works."], null, [], FolderPath: folder, BaseBranch: branch, Stage: stage);

            using var created = await client.SendAsync(HttpMethod.Post, tasks, Draft("First", repository, TaskStage.Ready));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var first = await created.ReadAsync<TaskDto>();
            Assert.Equal(TaskStage.Ready, first.Stage);
            Assert.Equal(repository, first.FolderPath);
            Assert.Equal("main", first.BaseBranch);
            using var second = await client.SendAsync(HttpMethod.Post, tasks, Draft("Second", repository, TaskStage.Ready));
            var secondTask = await second.ReadAsync<TaskDto>();

            using var noCriteria = await client.SendAsync(HttpMethod.Post, tasks, Draft("No criteria", repository, TaskStage.Ready, []));
            Assert.Equal(HttpStatusCode.Conflict, noCriteria.StatusCode);
            using var badStage = await client.SendAsync(HttpMethod.Post, tasks, Draft("Done already", repository, TaskStage.Done));
            Assert.Equal(HttpStatusCode.BadRequest, badStage.StatusCode);
            using var outside = await client.SendAsync(HttpMethod.Post, tasks, Draft("Outside", Path.GetTempPath(), TaskStage.Ready));
            Assert.Equal(HttpStatusCode.Forbidden, outside.StatusCode);
            using var missingBranch = await client.SendAsync(HttpMethod.Post, tasks, Draft("Branch", repository, TaskStage.Ready, branch: "nope"));
            Assert.Equal(HttpStatusCode.BadRequest, missingBranch.StatusCode);
            using var plain = await client.SendAsync(HttpMethod.Post, tasks, Draft("Intake", null, null));
            Assert.Equal(TaskStage.Intake, (await plain.ReadAsync<TaskDto>()).Stage);

            using (var scope = fixture.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var connection = Assert.Single(await db.GitConnections.Where(g => g.ProjectId == project.Id && g.ForTasks).ToListAsync());
                Assert.Equal(2, await db.WorkItems.CountAsync(t => t.RepositoryConnectionId == connection.Id));
            }

            async Task<RepositorySnapshot> ScheduleAsync(TaskDto task)
            {
                using var execution = await client.SendAsync(HttpMethod.Post, $"{ApiClient.TasksPath(project)}/{task.Id}/executions/", new ExecutionInput(task.Version), Guid.NewGuid().ToString());
                var scheduled = await execution.ReadAsync<ExecutionDto>();
                using var scope = fixture.Factory.Services.CreateScope();
                var row = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Executions.AsNoTracking().SingleAsync(e => e.Id == scheduled.Id);
                return JsonSerializer.Deserialize<RepositorySnapshot>(row.RepositorySnapshot!, ApiClient.JsonOptions)!;
            }
            var before = await ScheduleAsync(first);
            Assert.Equal(repository, before.LocalPath);
            await LocalIntegrationTests.GitAsync(repository, ["-c", "user.name=Fixture", "-c", "user.email=fixture@example.test", "commit", "--allow-empty", "-m", "Two"]);
            var after = await ScheduleAsync(secondTask);
            Assert.Equal(before.ConnectionId, after.ConnectionId);
            Assert.Equal(before.ConnectionVersion, after.ConnectionVersion);
            Assert.NotEqual(before.BaseCommit, after.BaseCommit);
        }
        finally
        {
            configuration["Local:AllowRepositorySelection"] = "false";
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }
}
