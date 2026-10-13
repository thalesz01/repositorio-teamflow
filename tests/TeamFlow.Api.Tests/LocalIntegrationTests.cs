using System.Net;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TeamFlow.Api.Features.Integrations;
using TeamFlow.Api.Http;
using Xunit;
using TeamFlow.Api.Features.Memory;

namespace TeamFlow.Api.Tests;

public sealed class LocalIntegrationTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    internal static string FixtureExecutable
    {
        get
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent ?? throw new InvalidOperationException("Repository root missing.");
            return Path.Combine(root.FullName, "tests", "CodexCliFixture", "bin", new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name, "net10.0", "CodexCliFixture.exe");
        }
    }

    [Fact]
    public async Task NativeCliReceivesStdinHasBoundedLifecycleAndDoesNotInventBillingAsync()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Codex:Executable"] = FixtureExecutable, ["Codex:EnableRequests"] = "true" }).Build();
        var model = new CodexTextModel(configuration);
        var profile = new AgentProfile { Provider = "codex", Model = "default", MaxInputBytes = 4096, MaxOutputTokens = 128, TimeoutSeconds = 5 };
        var reply = await model.GenerateAsync(profile, "unused", "& echo cannot-execute-this", 0, CancellationToken.None);
        Assert.Equal("{\"tool\":\"finish\"}", reply.Text); Assert.Null(reply.EstimatedCostUsd); Assert.Null(reply.InputTokens);
        var failure = await Assert.ThrowsAsync<ApiException>(() => model.GenerateAsync(profile, "", "fail", 0, CancellationToken.None));
        Assert.Equal("codex_failed", failure.Code); Assert.DoesNotContain("sensitive", failure.Message);
        Assert.Equal("provider_invalid_response", (await Assert.ThrowsAsync<ApiException>(() => model.GenerateAsync(profile, "", "empty", 0, CancellationToken.None))).Code);
        Assert.Equal("provider_timeout", (await Assert.ThrowsAsync<ApiException>(() => model.GenerateAsync(profile, "", "timeout", 0, CancellationToken.None))).Code);
        configuration["Codex:EnableRequests"] = "false";
        Assert.Equal("provider_disabled", (await Assert.ThrowsAsync<ApiException>(() => model.GenerateAsync(profile, "", "safe", 0, CancellationToken.None))).Code);
    }

    [Fact]
    public async Task LocalConfigurationChecksProjectAuthorityAndPinsCommittedBaseAsync()
    {
        using var client = fixture.Factory.CreateClient(); using var outsider = fixture.Factory.CreateClient();
        await client.RegisterAsync(); await outsider.RegisterAsync();
        var project = await client.CreateProjectAsync();
        var path = $"/api/v1/organizations/{project.OrganizationId}/projects/{project.Id}";
        var root = Path.Combine(Path.GetTempPath(), "teamflow-local-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await GitAsync(root, ["init", "-b", "main"]);
            await GitAsync(root, ["-c", "user.name=Fixture", "-c", "user.email=fixture@example.test", "commit", "--allow-empty", "-m", "Fixture"]);
            var configuration = fixture.Factory.Services.GetRequiredService<IConfiguration>();
            configuration[$"Integrations:{project.OrganizationId:D}:{project.Id:D}:Local:RepositoryPath"] = root;
            configuration[$"Integrations:{project.OrganizationId:D}:{project.Id:D}:Codex:Enabled"] = "true";
            configuration["Codex:Executable"] = FixtureExecutable;
            configuration["Codex:EnableRequests"] = "true";
            using var wrong = await client.SendAsync(HttpMethod.Post, path + "/repository/local", new LocalRepositoryInput(Path.GetTempPath(), "main"));
            Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
            using var foreign = await outsider.SendAsync(HttpMethod.Post, path + "/repository/local", new LocalRepositoryInput(root, "main"));
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
            using var missing = await client.SendAsync(HttpMethod.Post, path + "/repository/local", new LocalRepositoryInput(root, "missing"));
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            using var connected = await client.SendAsync(HttpMethod.Post, path + "/repository/local", new LocalRepositoryInput(root, "main"));
            var repository = await connected.ReadAsync<GitDto>();
            Assert.Equal("local", repository.Provider); Assert.Equal(root, repository.RemoteUrl); Assert.Equal(40, repository.BaseCommit.Length);
            using (var gitScope = fixture.Factory.Services.CreateScope())
            {
                var localGit = gitScope.ServiceProvider.GetRequiredService<LocalGit>();
                var connection = new GitConnection
                {
                    Id = repository.Id,
                    OrganizationId = project.OrganizationId,
                    ProjectId = project.Id,
                    Provider = "local",
                    LocalPath = root,
                    BaseBranch = "main",
                    BaseCommit = repository.BaseCommit
                };
                var executionId = Guid.NewGuid(); var content = "42";
                var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content)));
                var plan = new PublicationPlan(executionId, repository.BaseCommit, $"teamflow/{executionId:N}", "Local fixture", "Verified test fixture", DateTimeOffset.UtcNow, [new("answer.txt", content, hash)]);
                var published = await localGit.PublishAsync(connection, plan, CancellationToken.None);
                var replay = await localGit.PublishAsync(connection, plan, CancellationToken.None);
                Assert.Equal(published, replay); Assert.False(File.Exists(Path.Combine(root, "answer.txt")));
                var snapshot = new RepositorySnapshot(connection.Id, repository.Version, 0, root, "main", repository.BaseCommit, plan.Branch, root);
                var review = await localGit.ReviewAsync(connection, snapshot, published.HeadCommit, CancellationToken.None);
                Assert.True(review.CiPassed); Assert.False(review.Merged);
                Assert.Contains("+42", await localGit.DiffAsync(connection, repository.BaseCommit, published.HeadCommit, CancellationToken.None));
                await GitAsync(root, ["update-ref", "refs/heads/main", published.HeadCommit, repository.BaseCommit]);
                var merged = await localGit.ReviewAsync(connection, snapshot, published.HeadCommit, CancellationToken.None);
                Assert.True(merged.Merged); Assert.Equal(repository.BaseCommit, merged.MergeBaseParent);
                Assert.Equal("base_changed", (await Assert.ThrowsAsync<ApiException>(() => localGit.PublishAsync(connection, plan, CancellationToken.None))).Code);
                await GitAsync(root, ["update-ref", "refs/heads/main", repository.BaseCommit, published.HeadCommit]);
                await GitAsync(root, ["update-ref", "refs/heads/" + plan.Branch, repository.BaseCommit, published.HeadCommit]);
                Assert.Equal("branch_conflict", (await Assert.ThrowsAsync<ApiException>(() => localGit.PublishAsync(connection, plan, CancellationToken.None))).Code);
            }
            using var duplicate = await client.SendAsync(HttpMethod.Post, path + "/repository/local", new LocalRepositoryInput(root, "main"));
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            using var created = await client.SendAsync(HttpMethod.Post, path + "/profiles/machine", new { });
            var profile = await created.ReadAsync<ProfileDto>(); Assert.Equal("codex", profile.Provider);
            using var replayBinding = await client.SendAsync(HttpMethod.Post, path + "/profiles/machine", new { });
            Assert.Equal(profile.Id, (await replayBinding.ReadAsync<ProfileDto>()).Id);
            using var retired = await client.SendAsync(HttpMethod.Post, path + "/profiles/codex", new { });
            Assert.Equal(HttpStatusCode.Gone, retired.StatusCode);
            using var memoryResponse = await client.SendAsync(HttpMethod.Post, path + "/memory/", new MemoryInput("docs/local.md", "Local convention", "Local convention requires verified tests."));
            var candidate = await memoryResponse.ReadAsync<MemoryDetail>();
            using var validatedResponse = await client.SendAsync(HttpMethod.Put, path + $"/memory/{candidate.Item.Id}",
                new MemoryUpdate(candidate.Item.Version, candidate.Item.Title, candidate.Content, MemoryStatus.Validated));
            var validated = await validatedResponse.ReadAsync<MemoryDetail>();
            using var indexed = await client.SendAsync(HttpMethod.Post, path + $"/memory/{validated.Item.Id}/indexes", new IndexInput(validated.Item.Version, profile.Id, "local-text-index", 0.05m));
            var job = await indexed.ReadAsync<IndexDto>(); Assert.Equal("text-only", job.Model); Assert.Equal(0, job.Dimensions);
            using (var memoryScope = fixture.Factory.Services.CreateScope())
                Assert.True(await memoryScope.ServiceProvider.GetRequiredService<MemoryIndexAccess>().ProcessAsync(CancellationToken.None));
            using var searchResponse = await client.SendAsync(HttpMethod.Post, path + "/memory/search", new SearchInput("local convention"));
            var bundle = await searchResponse.ReadAsync<ContextBundle>(); Assert.Single(bundle.Sources);
            using var semanticResponse = await client.SendAsync(HttpMethod.Post, path + "/memory/search", new SearchInput("local", Enumerable.Repeat(1f, 1536).ToArray()));
            Assert.Empty((await semanticResponse.ReadAsync<ContextBundle>()).Sources);
            using var scope = fixture.Factory.Services.CreateScope();
            var access = scope.ServiceProvider.GetRequiredService<ProfileAccess>();
            Assert.True(await access.IsLocalAsync(project.OrganizationId, project.Id, profile.Id, CancellationToken.None));
            Assert.Equal("embedding_not_supported", (await Assert.ThrowsAsync<ApiException>(() => access.EmbedAsync(project.OrganizationId, project.Id, profile.Id, ["memory"], 1, CancellationToken.None))).Code);
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
    }

    internal static async Task GitAsync(string path, string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = path, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Git unavailable.");
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(); await Task.WhenAll(stdout, stderr); Assert.Equal(0, process.ExitCode);
    }
}
