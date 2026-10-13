using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Tasks;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Executions;

public sealed record ProgressEvent(string EventId, Guid ExecutionId, long Sequence, int Version,
    DateTimeOffset Timestamp, int Attempt, string Kind, string? Tool, string? Path, bool? Succeeded, string? Hash);
public sealed record ExecutionProgress(ExecutionDto Execution, decimal? EstimatedCostUsd, int? InputTokens,
    int? OutputTokens, ProgressEvent[] Events, string? NextCursor);

// Project progress deliberately excludes prompts, file contents and arbitrary tool output.
public sealed class ProgressAccess(AppDbContext db, TaskAccess tasks, TimeProvider clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ExecutionProgress> ReadAsync(Guid organizationId, Guid projectId, Guid taskId, Guid id,
        string? after, ClaimsPrincipal user, CancellationToken ct)
    {
        await tasks.RequireAsync(organizationId, projectId, taskId, user, ct);
        var execution = await db.Executions.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id
            && e.OrganizationId == organizationId && e.ProjectId == projectId && e.TaskId == taskId, ct)
            ?? throw new ApiException(404, "execution_not_found", "Execution not found.");
        var sequence = Cursor(after);
        var oldest = clock.GetUtcNow().AddDays(-30);
        var checkpoints = await db.ExecutionCheckpoints.AsNoTracking().Where(c => c.ExecutionId == id
            && c.OrganizationId == organizationId && c.ProjectId == projectId && c.CreatedAt >= oldest
            && (long)c.Attempt * 1000 + c.Sequence > sequence)
            .OrderBy(c => c.Attempt).ThenBy(c => c.Sequence).Take(100).ToListAsync(ct);
        var events = checkpoints.Select(c =>
        {
            var context = JsonSerializer.Deserialize<CheckpointContext>(c.Context, JsonOptions)
                ?? throw new InvalidDataException("Invalid persisted checkpoint.");
            var evidence = context.Evidence.LastOrDefault();
            var number = (long)c.Attempt * 1000 + c.Sequence;
            return new ProgressEvent(number.ToString(CultureInfo.InvariantCulture), id, number, 1, c.CreatedAt,
                c.Attempt, c.Sequence == 0 ? "started" : "checkpoint", evidence?.Tool, evidence?.Path, evidence?.Succeeded, evidence?.Hash);
        }).ToArray();
        var turns = await db.ModelTurns.AsNoTracking().Where(t => t.ExecutionId == id
            && t.OrganizationId == organizationId && t.ProjectId == projectId)
            .Select(t => new { t.EstimatedCostUsd, t.InputTokens, t.OutputTokens }).Take(200).ToListAsync(ct);
        return new(ExecutionScheduler.ToDto(execution),
            turns.Any(t => t.EstimatedCostUsd == null) ? null : turns.Sum(t => t.EstimatedCostUsd ?? 0),
            turns.Any(t => t.InputTokens == null) ? null : turns.Sum(t => t.InputTokens ?? 0),
            turns.Any(t => t.OutputTokens == null) ? null : turns.Sum(t => t.OutputTokens ?? 0),
            events, events.LastOrDefault()?.EventId ?? after);
    }

    public async Task<byte[]> ReportAsync(Guid organizationId, Guid projectId, Guid taskId, Guid id,
        string eventId, ClaimsPrincipal user, CancellationToken ct)
    {
        await ReadAsync(organizationId, projectId, taskId, id, null, user, ct);
        var number = Cursor(eventId);
        var attempt = (int)(number / 1000);
        var sequence = (int)(number % 1000);
        var oldest = clock.GetUtcNow().AddDays(-30);
        var checkpoint = await db.ExecutionCheckpoints.AsNoTracking().SingleOrDefaultAsync(c => c.ExecutionId == id
            && c.OrganizationId == organizationId && c.ProjectId == projectId && c.Attempt == attempt
            && c.Sequence == sequence && c.CreatedAt >= oldest, ct)
            ?? throw new ApiException(404, "report_not_found", "Report not found or expired.");
        var context = JsonSerializer.Deserialize<CheckpointContext>(checkpoint.Context, JsonOptions)
            ?? throw new InvalidDataException("Invalid persisted checkpoint.");
        var evidence = context.Evidence.LastOrDefault();
        int? exitCode = null;
        bool? timedOut = null;
        bool? truncated = null;
        if (evidence?.Tool == "test")
        {
            try
            {
                using var detail = JsonDocument.Parse(evidence.Detail);
                if (detail.RootElement.TryGetProperty("ExitCode", out var exit) && exit.TryGetInt32(out var parsed)) exitCode = parsed;
                if (detail.RootElement.TryGetProperty("TimedOut", out var timeout) && timeout.ValueKind is JsonValueKind.True or JsonValueKind.False) timedOut = timeout.GetBoolean();
                if (detail.RootElement.TryGetProperty("OutputTruncated", out var limited) && limited.ValueKind is JsonValueKind.True or JsonValueKind.False) truncated = limited.GetBoolean();
            }
            catch (JsonException) { /* Older evidence can contain a plain status; it remains unverified metadata. */ }
        }
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            executionId = id,
            eventId,
            attempt,
            sequence,
            checkpoint.CreatedAt,
            context.BaseCommit,
            checkpoint.ContentHash,
            evidence?.Tool,
            evidence?.Path,
            evidence?.Succeeded,
            evidence?.Hash,
            exitCode,
            timedOut,
            outputTruncated = truncated,
            note = "Controller evidence; does not constitute human approval or confirmed merge. Raw output omitted."
        }, JsonOptions);
    }
    private static long Cursor(string? value)
    {
        if (value is null) return 0;
        if (value.Length > 10 || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            || parsed < 0 || parsed > 5200)
            throw new ApiException(400, "invalid_cursor", "Invalid progress cursor.");
        return parsed;
    }
}

public static class ProgressEndpoints
{
    public static void MapProgress(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/organizations/{organizationId:guid}/projects/{projectId:guid}/tasks/{taskId:guid}/executions/{executionId:guid}")
            .WithTags("Executions").RequireAuthorization();
        group.MapGet("/progress", async (Guid organizationId, Guid projectId, Guid taskId, Guid executionId, string? after,
            ClaimsPrincipal user, ProgressAccess progress, CancellationToken ct) =>
            await progress.ReadAsync(organizationId, projectId, taskId, executionId, after, user, ct)).Produces<ExecutionProgress>();
        group.MapGet("/reports/{eventId}", async (Guid organizationId, Guid projectId, Guid taskId, Guid executionId,
            string eventId, ClaimsPrincipal user, ProgressAccess progress, CancellationToken ct) =>
            Results.File(await progress.ReportAsync(organizationId, projectId, taskId, executionId, eventId, user, ct),
                "application/json", $"execution-{executionId:N}-{eventId}.json"));
        group.MapGet("/events", StreamAsync).Produces(StatusCodes.Status200OK, contentType: "text/event-stream");
    }

    private static async Task StreamAsync(Guid organizationId, Guid projectId, Guid taskId, Guid executionId,
        HttpContext http, ProgressAccess progress, CancellationToken ct)
    {
        string? cursor = http.Request.Headers["Last-Event-ID"].FirstOrDefault();
        // Authorize and validate the cursor before committing the streaming headers.
        var snapshot = await progress.ReadAsync(organizationId, projectId, taskId, executionId, cursor, http.User, ct);
        http.Response.ContentType = "text/event-stream";
        http.Response.Headers["X-Accel-Buffering"] = "no";
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        json.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        try
        {
            // Bounded connections force fresh authorization; EventSource reconnects with its last event ID.
            for (var tick = 0; tick < 30; tick++)
            {
                foreach (var item in snapshot.Events)
                    await http.Response.WriteAsync($"id: {item.EventId}\nevent: progress\ndata: {JsonSerializer.Serialize(item, json)}\n\n", ct);
                await http.Response.WriteAsync($"event: state\ndata: {JsonSerializer.Serialize(snapshot with { Events = [] }, json)}\n\n", ct);
                await http.Response.Body.FlushAsync(ct);
                cursor = snapshot.NextCursor;
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                snapshot = await progress.ReadAsync(organizationId, projectId, taskId, executionId, cursor, http.User, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (ApiException) { await http.Response.WriteAsync("event: access-ended\ndata: {}\n\n", ct); }
    }
}
