using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Integrations;

public sealed record TextModelResult(string Text, int? InputTokens, int? OutputTokens, decimal? EstimatedCostUsd);
public interface ITextModel
{
    Task<TextModelResult> GenerateAsync(AgentProfile profile, string apiKey, string prompt, decimal budgetUsd, CancellationToken ct);
}

public sealed class OpenAiTextModel(IHttpClientFactory clients, IConfiguration configuration) : ITextModel
{
    public async Task<TextModelResult> GenerateAsync(AgentProfile profile, string apiKey, string prompt, decimal budgetUsd, CancellationToken ct)
    {
        if (!configuration.GetValue<bool>("OpenAI:EnableRequests"))
            throw new ApiException(503, "provider_disabled", "External model calls are disabled on this server.");
        if (profile.RevokedAt is not null || profile.MaxInputBytes is < 256 or > 65536
            || profile.MaxOutputTokens is < 16 or > 8192 || profile.TimeoutSeconds is < 5 or > 120
            || profile.InputUsdPerMillionTokens <= 0 || profile.OutputUsdPerMillionTokens <= 0)
            throw new ApiException(409, "profile_unavailable", "The model profile is not available.");
        var inputBytes = Encoding.UTF8.GetByteCount(prompt);
        if (inputBytes == 0 || inputBytes > profile.MaxInputBytes)
            throw new ApiException(400, "context_limit", "Context exceeds the profile input limit.");
        // Conservative admission estimate; actual billing is controlled by the provider.
        var estimate = ((inputBytes + 1024m) * profile.InputUsdPerMillionTokens
            + profile.MaxOutputTokens * profile.OutputUsdPerMillionTokens) / 1_000_000m;
        if (budgetUsd < estimate) throw new ApiException(409, "budget_exceeded", "Available budget is below the configured request estimate.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(profile.TimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses")
        {
            Content = JsonContent.Create(new { model = profile.Model, input = prompt, max_output_tokens = profile.MaxOutputTokens, store = false })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        try
        {
            using var response = await clients.CreateClient("OpenAI").SendAsync(request, linked.Token);
            if (!response.IsSuccessStatusCode) throw new ApiException(502, "provider_rejected", "The model provider rejected the request.");
            using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(linked.Token));
            var root = document.RootElement;
            if (!root.TryGetProperty("status", out var status) || status.GetString() != "completed")
                throw new ApiException(502, "provider_incomplete", "The model response is incomplete.");
            var text = new StringBuilder();
            foreach (var item in root.GetProperty("output").EnumerateArray())
            {
                if (item.GetProperty("type").GetString() != "message") continue;
                foreach (var content in item.GetProperty("content").EnumerateArray())
                    if (content.GetProperty("type").GetString() == "output_text") text.Append(content.GetProperty("text").GetString());
            }
            if (text.Length == 0) throw new ApiException(502, "provider_empty", "The provider returned no text output.");
            int? inputTokens = null, outputTokens = null;
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                inputTokens = usage.GetProperty("input_tokens").GetInt32();
                outputTokens = usage.GetProperty("output_tokens").GetInt32();
                if (inputTokens < 0 || outputTokens < 0) throw new JsonException("Invalid usage.");
            }
            decimal? cost = inputTokens is not null && outputTokens is not null
                ? (inputTokens.Value * profile.InputUsdPerMillionTokens + outputTokens.Value * profile.OutputUsdPerMillionTokens) / 1_000_000m : null;
            return new(text.ToString(), inputTokens, outputTokens, cost);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new ApiException(504, "provider_timeout", "The model request timed out."); }
        catch (HttpRequestException) { throw new ApiException(502, "provider_unavailable", "The model provider is unavailable."); }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        { throw new ApiException(502, "provider_invalid_response", "The model provider returned an unsupported response."); }
    }
}
