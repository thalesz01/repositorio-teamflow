using System.Text.Json;

namespace TeamFlow.Runner;

public static class TestEvidence
{
    public static bool IsRecoverable(ToolEvidence evidence)
    {
        if (evidence.Tool != "test" || evidence.Succeeded) return false;
        try
        {
            using var detail = JsonDocument.Parse(evidence.Detail);
            var root = detail.RootElement;
            return root.GetProperty("ExitCode").GetInt32() != 0
                && !root.GetProperty("TimedOut").GetBoolean() && !root.GetProperty("OutputTruncated").GetBoolean();
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { return false; }
    }

    public static bool SameValidation(string saved, string current)
    {
        try
        {
            var left = System.Text.Json.Nodes.JsonNode.Parse(saved)?.AsObject();
            var right = System.Text.Json.Nodes.JsonNode.Parse(current)?.AsObject();
            if (left is null || right is null) return false;
            left.Remove("StandardOutput"); left.Remove("StandardError");
            right.Remove("StandardOutput"); right.Remove("StandardError");
            return System.Text.Json.Nodes.JsonNode.DeepEquals(left, right);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { return saved == current; }
    }
    public static bool IsValidated(PortableContext context)
    {
        var edit = Array.FindLastIndex(context.Evidence, e => e.Tool == "write" && e.Succeeded);
        var test = Array.FindLastIndex(context.Evidence, e => e.Tool == "test");
        return edit >= 0 && test > edit && context.Evidence[test].Succeeded && context.Pending.Length == 0
            && context.Evidence.All(e => e.Succeeded || IsRecoverable(e));
    }
}
