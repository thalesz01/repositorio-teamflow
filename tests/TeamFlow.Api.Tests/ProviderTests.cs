using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TeamFlow.Api.Features.Integrations;
using TeamFlow.Api.Http;
using Xunit;

namespace TeamFlow.Api.Tests;

public sealed class ProviderTests
{
    private static AgentProfile Profile => new()
    {
        Model = "fixture-model",
        MaxInputBytes = 4096,
        MaxOutputTokens = 128,
        TimeoutSeconds = 5,
        InputUsdPerMillionTokens = 1,
        OutputUsdPerMillionTokens = 2
    };
    private static IConfiguration Enabled => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["OpenAI:EnableRequests"] = "true" }).Build();

    [Fact]
    public async Task ResponsesAdapterUsesBoundedStatelessRequestsAndReportsUnknownUsageAsync()
    {
        var calls = 0;
        var clients = new FixtureHttpClients(async (request, ct) =>
        {
            calls++;
            Assert.Equal("https://api.openai.com/v1/responses", request.RequestUri?.AbsoluteUri);
            Assert.Equal("fixture-key", request.Headers.Authorization?.Parameter);
            var body = await Assert.IsType<JsonContent>(request.Content).ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            Assert.False(body.GetProperty("store").GetBoolean());
            Assert.Equal(128, body.GetProperty("max_output_tokens").GetInt32());
            return Response(new
            {
                status = "completed",
                output = new[] { new { type = "message", role = "assistant",
                content = new[] { new { type = "output_text", text = "Fixture result" } } } },
                usage = (object?)null
            });
        });
        var model = new OpenAiTextModel(clients, Enabled);
        Assert.Equal("budget_exceeded", (await Assert.ThrowsAsync<ApiException>(() => model.GenerateAsync(Profile, "fixture-key", "Prompt", 0m, CancellationToken.None))).Code);
        Assert.Equal(0, calls);
        var result = await model.GenerateAsync(Profile, "fixture-key", "Prompt", 1m, CancellationToken.None);
        Assert.Equal("Fixture result", result.Text);
        Assert.Null(result.EstimatedCostUsd);
        Assert.Null(result.InputTokens);
        Assert.Equal(1, calls);
        Assert.Equal("context_limit", (await Assert.ThrowsAsync<ApiException>(() => model.GenerateAsync(Profile, "fixture-key", new string('x', 5000), 1m, CancellationToken.None))).Code);
    }

    [Fact]
    public async Task ProviderFailuresDoNotLeakBodiesAndCancellationIsPropagatedAsync()
    {
        var rejected = new OpenAiTextModel(new FixtureHttpClients((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        { Content = new StringContent("fixture-key should never be exposed") })), Enabled);
        var failure = await Assert.ThrowsAsync<ApiException>(() => rejected.GenerateAsync(Profile, "fixture-key", "Prompt", 1m, CancellationToken.None));
        Assert.Equal("provider_rejected", failure.Code);
        Assert.DoesNotContain("fixture-key", failure.Message);
        var waiting = new OpenAiTextModel(new FixtureHttpClients(async (_, ct) => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return Response(new { }); }), Enabled);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.GenerateAsync(Profile, "fixture-key", "Prompt", 1m, cancelled.Token));
        Assert.Equal("provider_timeout", (await Assert.ThrowsAsync<ApiException>(() => waiting.GenerateAsync(Profile, "fixture-key", "Prompt", 1m, CancellationToken.None))).Code);
        var incomplete = new OpenAiTextModel(new FixtureHttpClients((_, _) => Task.FromResult(Response(new { status = "incomplete" }))), Enabled);
        Assert.Equal("provider_incomplete", (await Assert.ThrowsAsync<ApiException>(() => incomplete.GenerateAsync(Profile, "fixture-key", "Prompt", 1m, CancellationToken.None))).Code);
    }

    [Fact]
    public async Task GitHubSignsJwtAndRestrictsInstallationTokenToOneReadOnlyRepositoryAsync()
    {
        using var rsa = RSA.Create(2048);
        var connection = new GitConnection { AppId = 123, InstallationId = 456, RepositoryId = 789, Owner = "fixture", Repository = "repo", BaseBranch = "main" };
        var calls = 0;
        var clients = new FixtureHttpClients(async (request, ct) =>
        {
            calls++;
            if (request.Method == HttpMethod.Post)
            {
                var jwt = request.Headers.Authorization?.Parameter ?? throw new InvalidOperationException("Missing JWT.");
                var parts = jwt.Split('.');
                Assert.True(rsa.VerifyData(System.Text.Encoding.UTF8.GetBytes(parts[0] + "." + parts[1]), Decode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
                using var payload = JsonDocument.Parse(Decode(parts[1]));
                Assert.Equal("123", payload.RootElement.GetProperty("iss").GetString());
                var body = await Assert.IsType<JsonContent>(request.Content).ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
                Assert.Equal(789, Assert.Single(body.GetProperty("repository_ids").EnumerateArray()).GetInt64());
                Assert.Equal("read", body.GetProperty("permissions").GetProperty("contents").GetString());
                return Response(new { token = "fixture-github-token", expires_at = DateTimeOffset.UtcNow.AddHours(1) });
            }
            Assert.Equal("fixture-github-token", request.Headers.Authorization?.Parameter);
            var uri = request.RequestUri ?? throw new InvalidOperationException("Missing URI.");
            return uri.AbsolutePath.Contains("/git/ref/", StringComparison.Ordinal)
                ? Response(new { @object = new { sha = new string('a', 40) } })
                : Response(new { id = 789, full_name = "fixture/repo" });
        });
        var provider = new GitHubProvider(clients, TimeProvider.System);
        Assert.Equal(new string('a', 40), await provider.VerifyAsync(connection, rsa.ExportPkcs8PrivateKeyPem(), CancellationToken.None));
        Assert.Equal(3, calls);
    }

    private static byte[] Decode(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
    private static HttpResponseMessage Response(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
}

public sealed class FixtureHttpClients(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(new FixtureHandler(send)) { Timeout = TimeSpan.FromSeconds(20) };
    private sealed class FixtureHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct);
    }
}
