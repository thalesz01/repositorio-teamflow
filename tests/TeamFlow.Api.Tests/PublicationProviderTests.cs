using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TeamFlow.Api.Features.Integrations;
using TeamFlow.Api.Http;
using Xunit;

namespace TeamFlow.Api.Tests;

public sealed class PublicationProviderTests
{
    [Fact]
    public async Task LostPrResponseReconcilesWithoutCreatingAnotherPrOrReplacingBranchAsync()
    {
        using var handler = new GitHubFixture { LosePrResponse = true };
        using var client = new HttpClient(handler) { MaxResponseContentBufferSize = 1024 * 1024 };
        var provider = new GitHubPublisher(new ClientFactory(client), new GitHubProvider(new ClientFactory(client), TimeProvider.System));
        using var rsa = RSA.Create(2048);
        var connection = Connection();
        var plan = Plan();
        var failure = await Assert.ThrowsAsync<ApiException>(() => provider.PublishAsync(connection, rsa.ExportRSAPrivateKeyPem(), plan, default));
        Assert.Equal("git_unavailable", failure.Code);
        var receipt = await provider.PublishAsync(connection, rsa.ExportRSAPrivateKeyPem(), plan, default);
        Assert.Equal(7, receipt.Number);
        Assert.Equal(GitHubFixture.Head, receipt.HeadCommit);
        Assert.Equal(plan.BaseCommit, receipt.BaseCommit);
        Assert.Equal(1, handler.PrCreates);
        Assert.Equal(1, handler.BranchCreates);
        Assert.True(handler.PreservedExecutableMode);
        Assert.True(handler.WritePermissionsChecked);
    }

    [Fact]
    public async Task ChangedBaseAndConflictingBranchFailWithoutCreatingPrAsync()
    {
        using var rsa = RSA.Create(2048);
        foreach (var handler in new[] { new GitHubFixture { ChangedBase = true }, new GitHubFixture { ConflictingBranch = true }, new GitHubFixture { Symlink = true } })
        {
            using (handler)
            using (var client = new HttpClient(handler))
            {
                var factory = new ClientFactory(client);
                var provider = new GitHubPublisher(factory, new GitHubProvider(factory, TimeProvider.System));
                var error = await Assert.ThrowsAsync<ApiException>(() => provider.PublishAsync(Connection(), rsa.ExportRSAPrivateKeyPem(), Plan(), default));
                Assert.Equal(409, error.Status);
                Assert.Equal(0, handler.PrCreates);
            }
        }
    }

    [Fact]
    public async Task InvalidHashOrProtectedPathIsRejectedBeforeNetworkAsync()
    {
        using var handler = new GitHubFixture();
        using var client = new HttpClient(handler);
        var factory = new ClientFactory(client);
        var provider = new GitHubPublisher(factory, new GitHubProvider(factory, TimeProvider.System));
        var plan = Plan();
        foreach (var file in new[] { plan.Files[0] with { Hash = "wrong" }, plan.Files[0] with { Path = ".github/workflows/run.yml" } })
        {
            var error = await Assert.ThrowsAsync<ApiException>(() => provider.PublishAsync(Connection(), "unused", plan with { Files = [file] }, default));
            Assert.Equal("invalid_publication", error.Code);
        }
        Assert.Equal(0, handler.Requests);
    }

    private static GitConnection Connection() => new() { Owner = "fixture", Repository = "repo", RepositoryId = 1, AppId = 2, InstallationId = 3, BaseBranch = "main" };
    private static PublicationPlan Plan()
    {
        var id = Guid.NewGuid();
        return new(id, GitHubFixture.Base, $"teamflow/{id:N}", "Fix calculation", "Fixture evidence only; no real provider.",
            DateTimeOffset.Parse("2026-10-11T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            [new("answer.txt", "42", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("42"))))]);
    }
    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
    private sealed class GitHubFixture : HttpMessageHandler
    {
        public const string Base = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        public const string Head = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        private const string Tree = "cccccccccccccccccccccccccccccccccccccccc";
        private const string NewTree = "dddddddddddddddddddddddddddddddddddddddd";
        private string? _branch;
        private object? _pr;
        public bool LosePrResponse { get; init; }
        public bool ChangedBase { get; init; }
        public bool ConflictingBranch { get; init; }
        public bool Symlink { get; init; }
        public int Requests { get; private set; }
        public int PrCreates { get; private set; }
        public int BranchCreates { get; private set; }
        public bool PreservedExecutableMode { get; private set; }
        public bool WritePermissionsChecked { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            Assert.Equal("2026-03-10", request.Headers.GetValues("X-GitHub-Api-Version").Single());
            var path = Uri.UnescapeDataString(request.RequestUri?.AbsolutePath ?? "");
            if (path.StartsWith("/app/installations/", StringComparison.Ordinal))
            {
                using var body = JsonDocument.Parse(await (request.Content ?? throw new InvalidDataException("Missing fixture body.")).ReadAsStringAsync(ct));
                Assert.Equal(1, body.RootElement.GetProperty("repository_ids")[0].GetInt32());
                Assert.Equal("write", body.RootElement.GetProperty("permissions").GetProperty("contents").GetString());
                Assert.Equal("write", body.RootElement.GetProperty("permissions").GetProperty("pull_requests").GetString());
                WritePermissionsChecked = true;
                return Json(new { token = "fixture-write-token", expires_at = DateTimeOffset.UtcNow.AddMinutes(10) });
            }
            if (path == "/repos/fixture/repo") return Json(new { id = 1, full_name = "fixture/repo" });
            if (path.EndsWith("/git/ref/heads/main", StringComparison.Ordinal)) return Json(new { @object = new { sha = ChangedBase ? Head : Base } });
            if (path.Contains("/git/ref/heads/teamflow/", StringComparison.Ordinal))
                return ConflictingBranch ? Json(new { @object = new { sha = Tree } })
                    : _branch is null ? new(HttpStatusCode.NotFound) : Json(new { @object = new { sha = Head } });
            if (path.EndsWith("/git/commits/" + Base, StringComparison.Ordinal)) return Json(new { tree = new { sha = Tree } });
            if (path.EndsWith("/git/trees/" + Tree, StringComparison.Ordinal))
                return Json(new { truncated = false, tree = new[] { new { path = "answer.txt", mode = Symlink ? "120000" : "100755", type = "blob" } } });
            if (path.EndsWith("/git/trees", StringComparison.Ordinal))
            {
                using var body = JsonDocument.Parse(await (request.Content ?? throw new InvalidDataException("Missing fixture body.")).ReadAsStringAsync(ct));
                Assert.Equal(Tree, body.RootElement.GetProperty("base_tree").GetString());
                PreservedExecutableMode = body.RootElement.GetProperty("tree")[0].GetProperty("mode").GetString() == "100755";
                return Json(new { sha = NewTree });
            }
            if (path.EndsWith("/git/commits", StringComparison.Ordinal)) return Json(new { sha = Head });
            if (path.EndsWith("/git/refs", StringComparison.Ordinal))
            {
                BranchCreates++;
                using var body = JsonDocument.Parse(await (request.Content ?? throw new InvalidDataException("Missing fixture body.")).ReadAsStringAsync(ct));
                _branch = (body.RootElement.GetProperty("ref").GetString() ?? throw new InvalidDataException("Missing fixture ref."))["refs/heads/".Length..];
                return Json(new { @object = new { sha = Head } });
            }
            if (path.EndsWith("/pulls", StringComparison.Ordinal))
            {
                if (request.Method == HttpMethod.Get) return Json(_pr is null ? Array.Empty<object>() : new[] { _pr });
                PrCreates++;
                using var body = JsonDocument.Parse(await (request.Content ?? throw new InvalidDataException("Missing fixture body.")).ReadAsStringAsync(ct));
                Assert.True(body.RootElement.GetProperty("draft").GetBoolean());
                _pr = new
                {
                    number = 7,
                    html_url = "https://github.com/fixture/repo/pull/7",
                    body = body.RootElement.GetProperty("body").GetString(),
                    head = new { sha = Head, @ref = _branch, repo = new { id = 1 } },
                    @base = new { sha = Base, @ref = "main", repo = new { id = 1 } }
                };
                if (LosePrResponse && PrCreates == 1) throw new HttpRequestException("Fixture: response lost after effect.");
                return Json(_pr);
            }
            throw new InvalidOperationException("Unexpected fixture route: " + path);
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }
}
