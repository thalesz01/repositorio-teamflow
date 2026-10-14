using System.Text.Json;

namespace TeamFlow.Api.Features.Executions;

public static class TestEvidence
{
    public static bool IsRecoverable(CheckpointEvidence evidence)
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

    // An analysis is answered when the agent consulted the files and never changed or tested anything.
    public static bool IsAnswered(CheckpointContext context) =>
        context.Pending.Length == 0 && context.Evidence.Any(e => e.Tool is "read" or "search" && e.Succeeded)
        && !context.Evidence.Any(e => e.Tool is "write" or "test");

    public static bool IsValidated(CheckpointContext context)
    {
        var edit = Array.FindLastIndex(context.Evidence, e => e.Tool == "write" && e.Succeeded);
        var test = Array.FindLastIndex(context.Evidence, e => e.Tool == "test");
        return edit >= 0 && test > edit && context.Evidence[test].Succeeded && context.Pending.Length == 0
            && context.Evidence.All(e => e.Succeeded || IsRecoverable(e));
    }
}
