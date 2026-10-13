using Microsoft.Extensions.Configuration;
using TeamFlow.Api.Features.Integrations;
using TeamFlow.Api.Http;
using Xunit;

namespace TeamFlow.Api.Tests;

// Real Git repositories: the merge must move the base branch only when that cannot lose the person's work.
public sealed class LocalMergeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "teamflow-merge-" + Guid.NewGuid().ToString("N"));
    private readonly Guid _organization = Guid.NewGuid();
    private readonly Guid _project = Guid.NewGuid();
    private static readonly string[] Identity = ["-c", "user.name=Fixture", "-c", "user.email=fixture@example.test"];

    private sealed record Scene(LocalGit Git, GitConnection Connection, RepositorySnapshot Snapshot, string Head);

    private Task GitAsync(params string[] arguments) => LocalIntegrationTests.GitAsync(_root, arguments);
    private Task CommitAsync(string message) => GitAsync([.. Identity, "commit", "-q", "--allow-empty", "-m", message]);
    private string Read(string name) => File.ReadAllText(Path.Combine(_root, name));

    private async Task<string> RevAsync(string reference)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git", ["rev-parse", reference]) { WorkingDirectory = _root, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output.Trim();
    }

    private async Task<Scene> ArrangeAsync()
    {
        Directory.CreateDirectory(_root);
        await GitAsync("init", "-q", "-b", "main");
        File.WriteAllText(Path.Combine(_root, "a.txt"), "one");
        await GitAsync("add", "a.txt");
        await CommitAsync("base");
        var baseCommit = await RevAsync("HEAD");
        var branch = $"teamflow/{Guid.NewGuid():N}";
        await GitAsync("checkout", "-q", "-b", branch);
        File.WriteAllText(Path.Combine(_root, "a.txt"), "two");
        await GitAsync("add", "a.txt");
        await CommitAsync("execution result");
        var head = await RevAsync("HEAD");
        await GitAsync("checkout", "-q", "main");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { [$"Integrations:{_organization:D}:{_project:D}:Local:RepositoryPath"] = _root }).Build();
        var connection = new GitConnection { Id = Guid.NewGuid(), OrganizationId = _organization, ProjectId = _project, Provider = "local", LocalPath = _root, BaseBranch = "main", BaseCommit = baseCommit, Version = Guid.NewGuid() };
        var snapshot = new RepositorySnapshot(connection.Id, connection.Version, 0, _root, "main", baseCommit, branch, _root);
        return new(new LocalGit(new LocalRepository(configuration)), connection, snapshot, head);
    }

    [Fact]
    public async Task FastForwardsTheCheckedOutBaseBranchAndIsRepeatableAsync()
    {
        var scene = await ArrangeAsync();
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "untracked work stays");
        await scene.Git.MergeAsync(scene.Connection, scene.Snapshot, scene.Head, default);
        Assert.Equal(scene.Head, await RevAsync("main"));
        Assert.Equal("two", Read("a.txt"));
        Assert.Equal("untracked work stays", Read("notes.txt"));
        await scene.Git.MergeAsync(scene.Connection, scene.Snapshot, scene.Head, default);
        Assert.Equal(scene.Head, await RevAsync("main"));
    }

    [Fact]
    public async Task RefusesWhenTrackedFilesHaveLocalChangesAndChangesNothingAsync()
    {
        var scene = await ArrangeAsync();
        File.WriteAllText(Path.Combine(_root, "a.txt"), "local edit");
        var failure = await Assert.ThrowsAsync<ApiException>(() => scene.Git.MergeAsync(scene.Connection, scene.Snapshot, scene.Head, default));
        Assert.Equal("merge_requires_clean_checkout", failure.Code);
        Assert.Equal(scene.Snapshot.BaseCommit, await RevAsync("main"));
        Assert.Equal("local edit", Read("a.txt"));
    }

    [Fact]
    public async Task MovesOnlyTheReferenceWhenAnotherBranchIsCheckedOutAsync()
    {
        var scene = await ArrangeAsync();
        await GitAsync("checkout", "-q", "-b", "feature");
        File.WriteAllText(Path.Combine(_root, "a.txt"), "feature work");
        await GitAsync("add", "a.txt");
        await CommitAsync("feature");
        await scene.Git.MergeAsync(scene.Connection, scene.Snapshot, scene.Head, default);
        Assert.Equal(scene.Head, await RevAsync("main"));
        Assert.Equal("feature work", Read("a.txt"));
    }

    [Fact]
    public async Task RefusesWhenTheBaseBranchOrTheExecutionBranchMovedAfterTheReviewAsync()
    {
        var scene = await ArrangeAsync();
        await CommitAsync("someone else moved main");
        var moved = await Assert.ThrowsAsync<ApiException>(() => scene.Git.MergeAsync(scene.Connection, scene.Snapshot, scene.Head, default));
        Assert.Equal("base_changed", moved.Code);
        await GitAsync("branch", "-f", scene.Snapshot.Branch, scene.Snapshot.BaseCommit);
        var rewritten = await Assert.ThrowsAsync<ApiException>(() => scene.Git.MergeAsync(scene.Connection, scene.Snapshot, scene.Head, default));
        Assert.Equal("branch_conflict", rewritten.Code);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }
}
