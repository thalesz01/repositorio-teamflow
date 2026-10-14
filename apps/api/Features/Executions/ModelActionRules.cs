using System.Text;

namespace TeamFlow.Api.Features.Executions;

// Pure checks on what the runner sends and what the model answers. The model is untrusted: an action only runs if it
// fits the controller policy for this turn, so every rule here fails closed.
public static class ModelActionRules
{
    private const int MaxPolicyEntries = 100;
    private const int MaxCreatePatterns = 20;
    private const int MaxPathLength = 240;
    private const int MaxWriteBytes = 16384;

    public static bool IsValidPolicy(AgentToolPolicy? policy) =>
        policy is { Readable: { } readable, Editable: { } editable }
        && readable.Length <= MaxPolicyEntries && editable.Length <= MaxPolicyEntries && (policy.Create?.Length ?? 0) <= MaxCreatePatterns
        && readable.Concat(editable).Concat(policy.Create ?? []).All(IsSafePolicyPath);

    // Policy entries are relative paths or patterns: no drive letters, roots, empty segments or hidden (dot-prefixed) segments.
    private static bool IsSafePolicyPath(string path) =>
        !string.IsNullOrWhiteSpace(path) && path.Length <= MaxPathLength
        && !path.Contains(':') && !path.StartsWith('/') && !path.StartsWith('\\')
        && !path.Split('/', '\\').Any(segment => segment.StartsWith('.') || segment.Length == 0);

    public static bool IsValidAction(ModelAction? action, AgentToolPolicy policy, bool analysis)
    {
        if (action is null || action.Tool is not ("read" or "write" or "test" or "search" or "finish")) return false;
        if (analysis && action.Tool is "write" or "test") return false;
        return action.Tool switch
        {
            "search" => IsValidSearch(action),
            "read" => IsAllowedPath(action.Path, policy.Readable, policy),
            "write" => IsAllowedPath(action.Path, policy.Editable, policy) && IsValidWrite(action),
            _ => true
        };
    }

    private static bool IsValidSearch(ModelAction action) =>
        !string.IsNullOrWhiteSpace(action.Content) && action.Content.Length is >= 2 and <= 200;

    // A listed path, or a new file that matches a Create pattern.
    private static bool IsAllowedPath(string? path, string[] listed, AgentToolPolicy policy) =>
        path is not null && (listed.Contains(path) || (policy.Create ?? []).Any(pattern => ToolPathPattern.IsMatch(pattern, path)));

    // A write needs content within the size limit and the hash of the file it read, or "missing" for an approved new file.
    private static bool IsValidWrite(ModelAction action) =>
        action.Content is not null && Encoding.UTF8.GetByteCount(action.Content) <= MaxWriteBytes
        && action.ExpectedHash is { } hash && (hash == "missing" || (hash.Length == 64 && hash.All(Uri.IsHexDigit)));
}
