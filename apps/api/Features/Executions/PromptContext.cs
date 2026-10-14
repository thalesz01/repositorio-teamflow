using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TeamFlow.Api.Features.Executions;

/// <summary>
/// The context as the model sees it. Every step is a new CLI call that receives the whole context again, so what the evidence
/// carries is paid for at every later step. The persisted checkpoint stays complete (it is the portable record); only this view
/// is smaller: repeated reads and older test diagnostics are shortened, and once the file contents add up to <c>high</c> bytes the
/// oldest ones are replaced by a marker until they fit <c>low</c> again. Leaving the gap between the two means the text changes
/// rarely, so the provider's prompt cache keeps matching the unchanged beginning of the next call.
/// </summary>
public static class PromptContext
{
    public const int HighBytes = 40_000;
    public const int LowBytes = 20_000;
    private const int ShortLimit = 400;
    private const int OlderTestLimit = 300;
    private const int LatestTestLimit = 3000;
    // Plain UTF-8 text: the default encoder would spend six characters on every non-ASCII letter, <, > and &.
    private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Render(string context, int high = HighBytes, int low = LowBytes)
    {
        try
        {
            if (JsonNode.Parse(context) is not JsonObject root || root["evidence"] is not JsonArray array) return context;
            var entries = array.OfType<JsonObject>().ToList();
            Shorten(entries);
            Omit(entries, high, low);
            return root.ToJsonString(Compact);
        }
        catch (JsonException) { return context; }
    }

    private static string Detail(JsonObject entry) => entry["detail"]?.GetValue<string>() ?? "";
    private static string Tool(JsonObject entry) => entry["tool"]?.GetValue<string>() ?? "";
    private static bool Succeeded(JsonObject entry) => entry["succeeded"]?.GetValue<bool>() ?? false;

    private static void Shorten(List<JsonObject> entries)
    {
        var latestRead = new Dictionary<string, int>(StringComparer.Ordinal);
        var latestTest = -1;
        for (var i = 0; i < entries.Count; i++)
        {
            if (Tool(entries[i]) == "test") latestTest = i;
            if (Tool(entries[i]) == "read" && Succeeded(entries[i]) && entries[i]["path"]?.GetValue<string>() is { } path) latestRead[path] = i;
        }
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (Tool(entry) == "test") entry["detail"] = TestSummary(Detail(entry), i == latestTest ? LatestTestLimit : OlderTestLimit);
            else if (Tool(entry) == "read" && Succeeded(entry) && Detail(entry).Length > ShortLimit
                && entry["path"]?.GetValue<string>() is { } path && latestRead[path] != i)
                entry["detail"] = $"[superseded by the read of the same file at step {entries[latestRead[path]]["step"]}]";
        }
    }

    // The test evidence repeats the command, limits and sandbox manifest at every step; the model needs the outcome and the diagnostic.
    private static string TestSummary(string detail, int limit)
    {
        try
        {
            using var document = JsonDocument.Parse(detail);
            var root = document.RootElement;
            string Stream(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? (value.GetString() is { Length: > 0 } text ? (text.Length > limit ? text[..limit] + "…" : text) : "") : "";
            string Flag(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True ? $" {name}=true" : "";
            var exit = root.TryGetProperty("ExitCode", out var code) && code.ValueKind == JsonValueKind.Number ? code.GetInt32().ToString() : "?";
            return $"exit {exit}{Flag("TimedOut")}{Flag("OutputTruncated")}\nstdout: {Stream("StandardOutput")}\nstderr: {Stream("StandardError")}";
        }
        catch (JsonException) { return detail.Length > limit ? detail[..limit] + "…" : detail; }
    }

    // Replays the steps in order: the result for a prefix of the evidence is the same text in a longer evidence, so earlier calls still match.
    private static void Omit(List<JsonObject> entries, int high, int low)
    {
        var kept = new List<int>();
        var total = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            var size = Encoding.UTF8.GetByteCount(Detail(entries[i]));
            if (size > ShortLimit) { kept.Add(i); total += size; }
            if (total <= high) continue;
            // The newest result is what the model has to act on, so it is the last to go.
            while (total > low && kept.Count > 1)
            {
                var oldest = entries[kept[0]];
                total -= Encoding.UTF8.GetByteCount(Detail(oldest));
                oldest["detail"] = Tool(oldest) switch
                {
                    "read" => "[content omitted to save tokens; read the file again if you need it]",
                    "search" => "[results omitted to save tokens; search again if you need them]",
                    _ => "[output omitted to save tokens]"
                };
                kept.RemoveAt(0);
            }
        }
    }
}
