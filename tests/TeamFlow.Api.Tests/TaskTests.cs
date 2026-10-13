using System.Net;
using System.Net.Http.Json;
using TeamFlow.Api.Features.Tasks;
using TeamFlow.Api.Http;
using Xunit;

namespace TeamFlow.Api.Tests;

public sealed class TaskTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task TasksAreScopedPaginatedAndCannotBeCompletedManuallyAsync()
    {
        using var owner = fixture.Factory.CreateClient();
        using var outsider = fixture.Factory.CreateClient();
        await owner.RegisterAsync();
        await outsider.RegisterAsync();
        var project = await owner.CreateProjectAsync();
        var otherProject = await outsider.CreateProjectAsync();
        var task = await owner.CreateTaskAsync(project);
        await owner.CreateTaskAsync(project);
        var path = ApiClient.TasksPath(project);
        var page = await owner.GetFromJsonAsync<PageResult<TaskDto>>(path + "/?page=1&pageSize=1", ApiClient.JsonOptions);
        Assert.NotNull(page);
        Assert.Single(page.Items);
        Assert.Equal(2, page.Total);
        using var foreignRead = await outsider.GetAsync(path + $"/{task.Id}");
        Assert.Equal(HttpStatusCode.NotFound, foreignRead.StatusCode);
        using var crossScope = await outsider.GetAsync(ApiClient.TasksPath(otherProject) + $"/{task.Id}");
        Assert.Equal(HttpStatusCode.NotFound, crossScope.StatusCode);
        using var foreignWrite = await outsider.SendAsync(HttpMethod.Post, path + $"/{task.Id}/transition", new TransitionInput(TaskStage.Analysis, task.Version));
        Assert.Equal(HttpStatusCode.NotFound, foreignWrite.StatusCode);
        using var completion = await owner.SendAsync(HttpMethod.Post, path + $"/{task.Id}/transition", new TransitionInput(TaskStage.Done, task.Version));
        Assert.Equal(HttpStatusCode.Conflict, completion.StatusCode);
        using var analysis = await owner.SendAsync(HttpMethod.Post, path + $"/{task.Id}/transition", new TransitionInput(TaskStage.Analysis, task.Version));
        var analyzed = await analysis.ReadAsync<TaskDto>();
        using var stale = await owner.SendAsync(HttpMethod.Post, path + $"/{task.Id}/transition", new TransitionInput(TaskStage.Ready, task.Version));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var ready = await owner.SendAsync(HttpMethod.Post, path + $"/{task.Id}/transition", new TransitionInput(TaskStage.Ready, analyzed.Version));
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    [Fact]
    public async Task DependencyCyclesAndForeignProjectsAreRejectedAsync()
    {
        using var client = fixture.Factory.CreateClient();
        await client.RegisterAsync();
        var project = await client.CreateProjectAsync();
        var other = await client.CreateProjectAsync();
        var first = await client.CreateTaskAsync(project);
        var second = await client.CreateTaskAsync(project, [first.Id]);
        var foreign = await client.CreateTaskAsync(other);
        var path = ApiClient.TasksPath(project);
        var update = new TaskUpdate(first.Title, first.Description, first.AcceptanceCriteria, null, [second.Id], first.Version);
        using var cycle = await client.SendAsync(HttpMethod.Put, path + $"/{first.Id}", update);
        Assert.Equal(HttpStatusCode.Conflict, cycle.StatusCode);
        using var foreignDependency = await client.SendAsync(HttpMethod.Put, path + $"/{first.Id}", update with { DependencyIds = [foreign.Id] });
        Assert.Equal(HttpStatusCode.BadRequest, foreignDependency.StatusCode);
    }

    [Fact]
    public async Task ConcurrentEditsOnlyAcceptOneVersionAsync()
    {
        using var client = fixture.Factory.CreateClient();
        await client.RegisterAsync();
        var project = await client.CreateProjectAsync();
        var task = await client.CreateTaskAsync(project);
        var path = ApiClient.TasksPath(project) + $"/{task.Id}";
        var edit = new TaskUpdate("First edit", task.Description, task.AcceptanceCriteria, null, [], task.Version);
        var responses = await Task.WhenAll(client.SendAsync(HttpMethod.Put, path, edit), client.SendAsync(HttpMethod.Put, path, edit with { Title = "Second edit" }));
        try
        {
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }
}
