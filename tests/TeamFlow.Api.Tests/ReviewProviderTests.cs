using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TeamFlow.Api.Features.Integrations;
using Xunit;

namespace TeamFlow.Api.Tests;

public sealed class ReviewProviderTests
{
    [Theory]
    [InlineData(100, true)]
    [InlineData(999, false)]
    public async Task TrustedCiAppAndExactHeadAreRequiredAndReadTokenHasNoWritesAsync(long appId, bool passed)
    {
        using var handler = new FixtureHandler(appId);
        using var client = new HttpClient(handler);
        var factory = new ClientFactory(client);
        using var rsa = RSA.Create(2048);
        var reader = new GitHubReview(factory, new GitHubProvider(factory, TimeProvider.System));
        var result = await reader.ReadAsync(new() { Owner = "fixture", Repository = "repo", RepositoryId = 1, InstallationId = 3, AppId = 2, BaseBranch = "main" },
            rsa.ExportRSAPrivateKeyPem(), 7, "teamflow/fixture", new(100, ["build"]), default);
        Assert.Equal(passed, result.CiPassed);
        Assert.True(result.Merged);
        Assert.Equal(new string('a', 40), result.MergeBaseParent);
        Assert.Equal(new string('d', 40), result.MergeCommit);
        Assert.True(handler.CheckedPermissions);
    }

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => client; }
    private sealed class FixtureHandler(long appId) : HttpMessageHandler
    {
        public bool CheckedPermissions { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            object value;
            if (path.StartsWith("/app/installations/", StringComparison.Ordinal))
            {
                using var body = JsonDocument.Parse(await (request.Content ?? throw new InvalidDataException("Missing body.")).ReadAsStringAsync(ct));
                var permissions = body.RootElement.GetProperty("permissions");
                Assert.Equal("read", permissions.GetProperty("contents").GetString());
                Assert.Equal("read", permissions.GetProperty("pull_requests").GetString());
                Assert.Equal("read", permissions.GetProperty("checks").GetString());
                CheckedPermissions = true;
                value = new { token = "fixture-review-token", expires_at = DateTimeOffset.UtcNow.AddMinutes(10) };
            }
            else if (path.EndsWith("/pulls/7", StringComparison.Ordinal))
                value = new
                {
                    number = 7,
                    merged = true,
                    merge_commit_sha = new string('d', 40),
                    head = new { sha = new string('b', 40), @ref = "teamflow/fixture", repo = new { id = 1 } },
                    @base = new { sha = new string('d', 40), @ref = "main", repo = new { id = 1 } }
                };
            else if (path.EndsWith("/check-runs", StringComparison.Ordinal))
                value = new { total_count = 1, check_runs = new[] { new { id = 1, name = "build", status = "completed", conclusion = "success", head_sha = new string('b', 40), app = new { id = appId } } } };
            else if (path.EndsWith("/git/commits/" + new string('d', 40), StringComparison.Ordinal))
                value = new { parents = new[] { new { sha = new string('a', 40) }, new { sha = new string('b', 40) } } };
            else throw new InvalidOperationException("Unexpected fixture route.");
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
        }
    }
}
