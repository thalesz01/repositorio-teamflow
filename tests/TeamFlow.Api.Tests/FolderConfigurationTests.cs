using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TeamFlow.Api.Features.Integrations;
using TeamFlow.Api.Http;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Executions;
using TeamFlow.Api.Features.Tasks;
using TeamFlow.Runner;
using System.Text.Json;
using Xunit;

namespace TeamFlow.Api.Tests;

public sealed class FolderConfigurationTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecutionPreparesMissingGitAndRunnerCanCheckoutAndPublishAsync(bool withCode)
    {
        using var client = fixture.Factory.CreateClient(); await client.RegisterAsync();
        var project = await client.CreateProjectAsync();
        var root = Path.Combine(Path.GetTempPath(), "teamflow-bootstrap-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            if (withCode)
            {
                await File.WriteAllTextAsync(Path.Combine(root, "source.txt"), "existing source\r\n");
                await File.WriteAllTextAsync(Path.Combine(root, ".gitignore"), "ignored.txt\n");
                await File.WriteAllTextAsync(Path.Combine(root, "ignored.txt"), "ignored fixture");
                await File.WriteAllTextAsync(Path.Combine(root, ".env"), "private fixture");
                Directory.CreateDirectory(Path.Combine(root, "node_modules"));
                await File.WriteAllTextAsync(Path.Combine(root, "node_modules", "generated.txt"), "generated fixture");
            }
            var configuration = fixture.Factory.Services.GetRequiredService<IConfiguration>();
            configuration[$"Integrations:{project.OrganizationId}:{project.Id}:Local:RepositoryPath"] = root;
            var repositoryPath = $"/api/v1/organizations/{project.OrganizationId}/projects/{project.Id}/repository";
            using var saved = await client.SendAsync(HttpMethod.Post, repositoryPath + "/local/folder", new LocalFolderInput(root));
            var initial = await saved.ReadAsync<GitDto>();
            Assert.False(Directory.Exists(Path.Combine(root, ".git")));
            var task = await client.CreateTaskAsync(project);
            var tasksPath = ApiClient.TasksPath(project) + $"/{task.Id}";
            using var analyzed = await client.SendAsync(HttpMethod.Post, tasksPath + "/transition", new TransitionInput(TaskStage.Analysis, task.Version));
            using var ready = await client.SendAsync(HttpMethod.Post, tasksPath + "/transition", new TransitionInput(TaskStage.Ready, (await analyzed.ReadAsync<TaskDto>()).Version));
            var input = new ExecutionInput((await ready.ReadAsync<TaskDto>()).Version);
            using var scheduled = await client.SendAsync(HttpMethod.Post, tasksPath + "/executions/", input, "bootstrap");
            var execution = await scheduled.ReadAsync<ExecutionDto>(); Assert.Equal(ExecutionState.Queued, execution.State);
            using var replay = await client.SendAsync(HttpMethod.Post, tasksPath + "/executions/", input, "bootstrap");
            Assert.Equal(execution.Id, (await replay.ReadAsync<ExecutionDto>()).Id);
            using var scope = fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var record = await db.Executions.FindAsync(execution.Id); Assert.NotNull(record);
            var snapshot = JsonSerializer.Deserialize<RepositorySnapshot>(record.RepositorySnapshot!, new JsonSerializerOptions(JsonSerializerDefaults.Web)); Assert.NotNull(snapshot);
            Assert.Equal("main", snapshot.BaseBranch); Assert.Equal(40, snapshot.BaseCommit.Length); Assert.NotEqual(initial.Version, snapshot.ConnectionVersion);
            using var workspace = new ExecutionWorkspace(Path.Combine(Path.GetTempPath(), "teamflow-bootstrap-checkouts"));
            await new GitCheckout().PrepareAsync(workspace, new(root, snapshot.BaseCommit, snapshot.Branch, root), "local", CancellationToken.None);
            if (withCode)
            {
                Assert.Equal("existing source\r\n", await File.ReadAllTextAsync(workspace.Resolve("source.txt")));
                Assert.Equal("existing source\r\n", await File.ReadAllTextAsync(Path.Combine(root, "source.txt")));
                Assert.False(File.Exists(workspace.Resolve(".env"))); Assert.False(File.Exists(workspace.Resolve("ignored.txt")));
                Assert.False(Directory.Exists(workspace.Resolve("node_modules")));
                Assert.Equal("private fixture", await File.ReadAllTextAsync(Path.Combine(root, ".env")));
            }
            var connection = await db.GitConnections.FindAsync(snapshot.ConnectionId); Assert.NotNull(connection);
            var localGit = scope.ServiceProvider.GetRequiredService<LocalGit>();
            var content = "42";
            var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content)));
            var receipt = await localGit.PublishAsync(connection, new(execution.Id, snapshot.BaseCommit, snapshot.Branch, "Bootstrap fixture", "Review fixture", DateTimeOffset.UtcNow, [new("answer.txt", content, hash)]), CancellationToken.None);
            Assert.Contains("+42", await localGit.DiffAsync(connection, snapshot.BaseCommit, receipt.HeadCommit, CancellationToken.None));
            Assert.False(File.Exists(Path.Combine(root, "answer.txt")));
        }
        finally { foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal); Directory.Delete(root, true); }
    }

    [Fact]
    public async Task EmptyFolderPersistsWithoutGitAndBranchesAppearAfterExternalInitializationAsync()
    {
        using var client = fixture.Factory.CreateClient(); using var outsider = fixture.Factory.CreateClient();
        await client.RegisterAsync(); await outsider.RegisterAsync();
        var project = await client.CreateProjectAsync();
        var path = $"/api/v1/organizations/{project.OrganizationId}/projects/{project.Id}/repository";
        var root = Path.Combine(Path.GetTempPath(), "teamflow-folder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var configuration = fixture.Factory.Services.GetRequiredService<IConfiguration>();
            configuration[$"Integrations:{project.OrganizationId}:{project.Id}:Local:BrowseRoots:0"] = root;
            configuration[$"Integrations:{project.OrganizationId}:{project.Id}:Local:AllowRepositorySelection"] = "true";
            using var denied = await outsider.SendAsync(HttpMethod.Post, path + "/local/inspect", new LocalFolderInput(root));
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            using var inspection = await client.SendAsync(HttpMethod.Post, path + "/local/inspect", new LocalFolderInput(root));
            Assert.False((await inspection.ReadAsync<LocalFolderInspection>()).HasGit);
            using var saved = await client.SendAsync(HttpMethod.Post, path + "/local/folder", new LocalFolderInput(root));
            var connection = await saved.ReadAsync<GitDto>();
            Assert.Equal(root, connection.RemoteUrl); Assert.Empty(connection.BaseCommit); Assert.False(Directory.Exists(Path.Combine(root, ".git")));
            using var reloaded = await client.GetAsync(path + "/");
            Assert.Equal(connection.Id, (await reloaded.ReadAsync<GitDto>()).Id);
            await LocalIntegrationTests.GitAsync(root, ["init", "-b", "main"]);
            using var unborn = await client.SendAsync(HttpMethod.Post, path + "/local/inspect", new LocalFolderInput(root));
            var emptyGit = await unborn.ReadAsync<LocalFolderInspection>();
            Assert.True(emptyGit.HasGit); Assert.Empty(emptyGit.Branches);
            await LocalIntegrationTests.GitAsync(root, ["-c", "user.name=Fixture", "-c", "user.email=fixture@example.test", "commit", "--allow-empty", "-m", "Fixture"]);
            await LocalIntegrationTests.GitAsync(root, ["branch", "feature/local"]);
            using var branches = await client.SendAsync(HttpMethod.Post, path + "/local/inspect", new LocalFolderInput(root));
            var git = await branches.ReadAsync<LocalFolderInspection>();
            Assert.Equal(["feature/local", "main"], git.Branches); Assert.Equal("main", git.CurrentBranch);
            using var stale = await client.SendAsync(HttpMethod.Post, path + "/local/folder", new LocalFolderInput(root, "main", Guid.NewGuid()));
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            using var ready = await client.SendAsync(HttpMethod.Post, path + "/local/folder", new LocalFolderInput(root, "feature/local", connection.Version));
            var updated = await ready.ReadAsync<GitDto>(); Assert.Equal("feature/local", updated.BaseBranch); Assert.Equal(40, updated.BaseCommit.Length);
        }
        finally { foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal); Directory.Delete(root, true); }
    }
}
