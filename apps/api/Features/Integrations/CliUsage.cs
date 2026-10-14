using System.Text.Json;

namespace TeamFlow.Api.Features.Integrations;

/// <summary>Token counts a local CLI reported for one call. Input includes the cached part; a figure the CLI did not report stays null, never zero.</summary>
public sealed record CliUsage(int? Input, int? Cached, int? Output)
{
    private static int? Count(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var count) && count >= 0 ? count : null;

    /// <summary>Claude's JSON envelope: cache reads and cache writes are reported apart from input_tokens, so they are added back.</summary>
    public static CliUsage FromClaude(JsonElement envelope)
    {
        if (!envelope.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return new(null, null, null);
        var read = Count(usage, "cache_read_input_tokens") ?? 0;
        var input = Count(usage, "input_tokens") is { } plain ? plain + (Count(usage, "cache_creation_input_tokens") ?? 0) + read : (int?)null;
        return new(input, input is null ? null : read, Count(usage, "output_tokens"));
    }

    /// <summary>Codex <c>--json</c> prints one <c>turn.completed</c> event per turn; their usage is summed.</summary>
    public sealed class CodexCollector
    {
        private CliUsage? _total;

        public CliUsage Total => _total ?? new(null, null, null);

        public void Add(string line)
        {
            if (!line.Contains("\"turn.completed\"", StringComparison.Ordinal)) return;
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type) || type.GetString() != "turn.completed"
                    || !root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return;
                var turn = new CliUsage(Count(usage, "input_tokens"), Count(usage, "cached_input_tokens"), Count(usage, "output_tokens"));
                _total = _total is null ? turn : new(Sum(_total.Input, turn.Input), Sum(_total.Cached, turn.Cached), Sum(_total.Output, turn.Output));
            }
            catch (JsonException) { }
        }

        private static int? Sum(int? left, int? right) => left is null || right is null ? null : left + right;
    }
}
