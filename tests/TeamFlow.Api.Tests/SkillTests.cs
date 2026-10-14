using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Executions;
using TeamFlow.Api.Features.Tasks;
using Xunit;

namespace TeamFlow.Api.Tests;

public sealed class SkillTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task SkillsOfTheTaskFolderCanBeChosenForAnExecutionAndOnlyValidOnesAreAcceptedAsync()
    {
        using var client = fixture.Factory.CreateClient(); await client.RegisterAsync();
        var project = await client.CreateProjectAsync();
        var root = Path.Combine(Path.GetTempPath(), "teamflow-skills-" + Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(root, "app");
        foreach (var directory in new[] { ".claude/skills/review", ".agents/skills/lint", ".claude/skills/empty", ".claude/skills/huge", ".claude/skills/bad name" })
            Directory.CreateDirectory(Path.Combine(repository, directory));
        await File.WriteAllTextAsync(Path.Combine(repository, ".claude", "skills", "review", "SKILL.md"),
            "---\nname: code-review\ndescription: Review the change before finishing\n---\n# Review\nName every file you touch.\n");
        await File.WriteAllTextAsync(Path.Combine(repository, ".agents", "skills", "lint", "SKILL.md"), "Plain skill without a header.");
        await File.WriteAllTextAsync(Path.Combine(repository, ".claude", "skills", "huge", "SKILL.md"), new string('x', LocalSkills.MaxBytes + 1));
        await File.WriteAllTextAsync(Path.Combine(repository, ".claude", "skills", "bad name", "SKILL.md"), "Never offered.");
        var configuration = fixture.Factory.Services.GetRequiredService<IConfiguration>();
        try
        {
            await LocalIntegrationTests.GitAsync(repository, ["init", "-b", "main"]);
            await LocalIntegrationTests.GitAsync(repository, ["-c", "user.name=Fixture", "-c", "user.email=fixture@example.test", "commit", "--allow-empty", "-m", "One"]);
            configuration["Local:BrowseRoots:0"] = root; configuration["Local:AllowRepositorySelection"] = "true"; configuration["Local:UserSkills"] = "false";
            var tasks = ApiClient.TasksPath(project) + "/";
            using var created = await client.SendAsync(HttpMethod.Post, tasks, new TaskInput("With skill", "Do it", ["Works"], null, [], FolderPath: repository, Stage: TaskStage.Ready));
            var task = await created.ReadAsync<TaskDto>();

            var listed = await client.GetFromJsonAsync<SkillInfo[]>(tasks + task.Id + "/skills", ApiClient.JsonOptions);
            Assert.NotNull(listed);
            Assert.Equal(["project-claude:review", "project-agents:lint"], listed.Select(s => s.Id).ToArray());
            Assert.Equal("code-review", listed[0].Name);
            Assert.Equal("Review the change before finishing", listed[0].Description);
            Assert.Contains("Name every file", listed[0].Preview); Assert.DoesNotContain("---", listed[0].Preview);
            Assert.Equal("lint", listed[1].Name);

            async Task<HttpResponseMessage> RunAsync(string? skill) =>
                await client.SendAsync(HttpMethod.Post, tasks + task.Id + "/executions/", new ExecutionInput(task.Version, Skill: skill), Guid.NewGuid().ToString());
            foreach (var invalid in new[] { "project-claude:huge", "project-claude:bad name", "project-claude:../review", "project-claude:missing", "user-claude:review", "other:review", "review" })
            {
                using var refused = await RunAsync(invalid);
                Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            }
            using var accepted = await RunAsync("project-claude:review");
            var execution = await accepted.ReadAsync<ExecutionDto>();
            Assert.Equal("code-review", execution.Skill);
            using var scope = fixture.Factory.Services.CreateScope();
            var row = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Executions.AsNoTracking().SingleAsync(e => e.Id == execution.Id);
            var snapshot = JsonSerializer.Deserialize<SkillSnapshot>(row.SkillSnapshot!, ApiClient.JsonOptions);
            Assert.Contains("Name every file you touch.", snapshot!.Content);
            Assert.Equal("project", snapshot.Source); Assert.Equal(64, snapshot.Hash.Length);
        }
        finally
        {
            configuration["Local:AllowRepositorySelection"] = "false";
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }
}
