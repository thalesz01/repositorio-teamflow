using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TeamFlow.Api.Features.Integrations;
using TeamFlow.Api.Http;
using Xunit;

namespace TeamFlow.Api.Tests;

public sealed class EmbeddingProviderTests
{
    [Fact]
    public async Task AdapterIsOptInAndValidatesDimensionsOrderAndUsageAsync()
    {
        using var handler = new EmbeddingHandler(); using var client = new HttpClient(handler);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var model = new OpenAiEmbeddingModel(new ClientFactory(client), configuration);
        Assert.Equal("provider_disabled", (await Assert.ThrowsAsync<ApiException>(() => model.EmbedAsync("fixture-key", ["permitted text"], 1, default))).Code);
        Assert.Equal(0, handler.Calls);
        configuration["OpenAI:EnableRequests"] = "true"; configuration["Memory:EnableEmbeddingRequests"] = "true";
        var result = await model.EmbedAsync("fixture-key", ["permitted text"], 1, default);
        Assert.Single(result.Vectors); Assert.Equal(1536, result.Vectors[0].Length); Assert.Equal(10, result.InputTokens); Assert.Equal(0.00001m, result.EstimatedCostUsd);
        handler.Dimensions = 3;
        Assert.Equal("embedding_invalid_response", (await Assert.ThrowsAsync<ApiException>(() => model.EmbedAsync("fixture-key", ["permitted text"], 1, default))).Code);
        Assert.Equal("invalid_embedding_input", (await Assert.ThrowsAsync<ApiException>(() => model.EmbedAsync("fixture-key", [new string('x', 2049)], 1, default))).Code);
        Assert.Equal(2, handler.Calls);
    }
    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private sealed class EmbeddingHandler : HttpMessageHandler
    {
        public int Dimensions { get; set; } = 1536;
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; Assert.Equal("https://api.openai.com/v1/embeddings", request.RequestUri?.ToString());
            Assert.Equal("fixture-key", request.Headers.Authorization?.Parameter);
            using var body = JsonDocument.Parse(await (request.Content ?? throw new InvalidDataException()).ReadAsStringAsync(ct));
            Assert.Equal(1536, body.RootElement.GetProperty("dimensions").GetInt32());
            Assert.Equal("text-embedding-3-small", body.RootElement.GetProperty("model").GetString());
            Assert.Equal("permitted text", body.RootElement.GetProperty("input")[0].GetString());
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { model = "text-embedding-3-small", data = new[] { new { index = 0, embedding = Enumerable.Repeat(0.1f, Dimensions).ToArray() } }, usage = new { total_tokens = 10 } }) };
        }
    }
}
