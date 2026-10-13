using System.Net;
using System.Text.Json;

namespace TeamFlow.Runner;

public sealed class RunnerProtocolException(string code, HttpStatusCode status) : HttpRequestException("Runner operation was rejected.", null, status)
{
    public string Code { get; } = code;

    public static async Task RequireSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var code = "runner_failed";
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
            if (document.RootElement.TryGetProperty("code", out var field) && field.ValueKind == JsonValueKind.String
                && field.GetString() is { Length: > 0 and <= 64 } value && value.All(c => c == '_' || (c >= 'a' && c <= 'z')))
                code = value;
        }
        catch (JsonException) { }
        throw new RunnerProtocolException(code, response.StatusCode);
    }
}
