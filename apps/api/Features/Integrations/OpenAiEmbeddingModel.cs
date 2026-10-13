using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Integrations;

public sealed record EmbeddingResult(float[][] Vectors, int InputTokens, decimal EstimatedCostUsd);
public interface IEmbeddingModel
{
    Task<EmbeddingResult> EmbedAsync(string apiKey, string[] inputs, decimal usdPerMillionTokens, CancellationToken ct);
}

public sealed class OpenAiEmbeddingModel(IHttpClientFactory clients, IConfiguration configuration) : IEmbeddingModel
{
    public async Task<EmbeddingResult> EmbedAsync(string apiKey, string[] inputs, decimal usdPerMillionTokens, CancellationToken ct)
    {
        if (!configuration.GetValue<bool>("OpenAI:EnableRequests") || !configuration.GetValue<bool>("Memory:EnableEmbeddingRequests"))
            throw new ApiException(503, "provider_disabled", "Embedding calls are disabled.");
        if (inputs.Length is < 1 or > 32 || inputs.Any(s => string.IsNullOrWhiteSpace(s) || Encoding.UTF8.GetByteCount(s) > 2048)
            || usdPerMillionTokens <= 0)
            throw new ApiException(400, "invalid_embedding_input", "Embedding inputs exceed the configured bounds.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/embeddings")
        { Content = JsonContent.Create(new { model = "text-embedding-3-small", dimensions = 1536, encoding_format = "float", input = inputs }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await clients.CreateClient("OpenAI").SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new ApiException(502, "embedding_rejected", "Embedding provider rejected the request.");
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
            var root = document.RootElement;
            if (root.GetProperty("model").GetString() != "text-embedding-3-small") throw new JsonException("Unexpected model.");
            var entries = root.GetProperty("data").EnumerateArray().OrderBy(e => e.GetProperty("index").GetInt32()).ToArray();
            if (entries.Length != inputs.Length || entries.Where((e, i) => e.GetProperty("index").GetInt32() != i).Any())
                throw new JsonException("Unexpected vector count.");
            var vectors = entries.Select(e => e.GetProperty("embedding").EnumerateArray().Select(v => v.GetSingle()).ToArray()).ToArray();
            if (vectors.Any(v => v.Length != 1536 || v.Any(f => !float.IsFinite(f)) || v.All(f => f == 0)))
                throw new JsonException("Invalid vector.");
            var tokens = root.GetProperty("usage").GetProperty("total_tokens").GetInt32();
            if (tokens < 0) throw new JsonException("Invalid usage.");
            return new(vectors, tokens, tokens * usdPerMillionTokens / 1_000_000m);
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        { throw new ApiException(502, "embedding_invalid_response", "Embedding response could not be verified."); }
    }
}
