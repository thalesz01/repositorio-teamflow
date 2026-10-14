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
    int? OutputTokens, ProgressEvent[] Events, string? NextCursor, string? AgentSummary = null, int? CachedInputTokens = null);

// Project progress deliberately excludes prompts, file contents and arbitrary tool output.
public sealed class ProgressAccess(AppDbContext db, TaskAccess tasks, TimeProvider clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) } };

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
            .Select(t => new { t.EstimatedCostUsd, t.InputTokens, t.OutputTokens, t.CachedInputTokens, t.Attempt, t.Sequence, t.Action }).Take(200).ToListAsync(ct);
        return new(ExecutionScheduler.ToDto(execution),
            turns.Any(t => t.EstimatedCostUsd == null) ? null : turns.Sum(t => t.EstimatedCostUsd ?? 0),
            turns.Any(t => t.InputTokens == null) ? null : turns.Sum(t => t.InputTokens ?? 0),
            turns.Any(t => t.OutputTokens == null) ? null : turns.Sum(t => t.OutputTokens ?? 0),
            events, events.LastOrDefault()?.EventId ?? after, Summary(turns.OrderByDescending(t => t.Attempt).ThenByDescending(t => t.Sequence).Select(t => t.Action).FirstOrDefault()),
            turns.Any(t => t.CachedInputTokens == null) ? null : turns.Sum(t => t.CachedInputTokens ?? 0));
    }

    // The agent's own closing note: what it did and what the policy kept it from doing. Free text from the model, shown as plain text.
    private static string? Summary(string? action)
    {
        if (action is null) return null;
        try
        {
            using var document = JsonDocument.Parse(action);
            var root = document.RootElement;
            if (root.TryGetProperty("tool", out var tool) && tool.GetString() == "finish" && root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                return content.GetString() is { Length: > 0 } text ? (text.Length > 12000 ? text[..12000] : text) : null;
        }
        catch (JsonException) { }
        return null;
    }

    public async Task<byte[]> ReportAsync(Guid organizationId, Guid projectId, Guid taskId, Guid id,
        string? eventId, ClaimsPrincipal user, CancellationToken ct)
    {
        var progress = await ReadAsync(organizationId, projectId, taskId, id, null, user, ct);
        var oldest = clock.GetUtcNow().AddDays(-30);
        var query = db.ExecutionCheckpoints.AsNoTracking().Where(c => c.ExecutionId == id
            && c.OrganizationId == organizationId && c.ProjectId == projectId && c.CreatedAt >= oldest);
        if (eventId is not null)
        {
            var number = Cursor(eventId);
            var selectedAttempt = (int)(number / 1000); var selectedSequence = (int)(number % 1000);
            query = query.Where(c => c.Attempt == selectedAttempt && c.Sequence == selectedSequence);
        }
        var checkpoint = await query.OrderByDescending(c => c.Attempt).ThenByDescending(c => c.Sequence).FirstOrDefaultAsync(ct);
        if (eventId is not null && checkpoint is null) throw new ApiException(404, "report_not_found", "Report not found or expired.");
        var context = checkpoint is null ? null : JsonSerializer.Deserialize<CheckpointContext>(checkpoint.Context, JsonOptions);
        var failures = await db.ModelTurns.AsNoTracking().Where(t => t.ExecutionId == id
            && t.OrganizationId == organizationId && t.ProjectId == projectId && t.State != "completed")
            .OrderByDescending(t => t.Attempt).ThenByDescending(t => t.Sequence)
            .Select(t => new { t.Attempt, t.Sequence, t.OperationId, t.State, t.FailureCode, t.FailureType }).Take(200).ToListAsync(ct);
        var failureCode = failures.FirstOrDefault()?.FailureCode ?? progress.Execution.FailureCode;
        var evidence = context?.Evidence.LastOrDefault();
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            executionId = id,
            eventId,
            attempt = checkpoint?.Attempt ?? progress.Execution.Attempt,
            sequence = checkpoint?.Sequence,
            createdAt = checkpoint?.CreatedAt ?? progress.Execution.CreatedAt,
            baseCommit = context?.BaseCommit,
            contentHash = checkpoint?.ContentHash,
            evidence?.Tool,
            evidence?.Path,
            evidence?.Succeeded,
            evidence?.Hash,
            exitCode = TestField(evidence, "ExitCode"),
            timedOut = TestField(evidence, "TimedOut"),
            outputTruncated = TestField(evidence, "OutputTruncated"),
            execution = progress.Execution,

            objective = context?.Objective,
            acceptanceCriteria = context?.AcceptanceCriteria,
            pending = context?.Pending,
            agentSummary = progress.AgentSummary,
            failure = failureCode is null ? null : new { code = failureCode, phase = failures.Count > 0 ? "model" : "execution", message = FailureMessage(failureCode) },
            modelFailures = failures,
            steps = context?.Evidence.Select(item => new
            {
                item.Step,
                item.Tool,
                item.Path,
                item.Succeeded,
                item.Hash,
                diagnostic = Diagnostic(item)
            }),
            note = "File contents omitted; test diagnostics are bounded and redacted when captured. Older reports may not contain test output."
        }, JsonOptions);
    }
    private static JsonElement? TestField(CheckpointEvidence? evidence, string name)
    {
        if (evidence is null || Diagnostic(evidence) is not { ValueKind: JsonValueKind.Object } detail) return null;
        return detail.TryGetProperty(name, out var value) ? value : null;
    }
    private static string FailureMessage(string code) => code switch
    {
        "invalid_model_response" => "A IA retornou uma resposta fora do formato esperado. A interação foi interrompida antes de executar uma nova ação.",
        "invalid_model_action" => "A IA propôs uma ação que não atende à política desta execução.",
        "claude_failed" => "O Claude não concluiu a geração da resposta. Verifique login, disponibilidade do modelo e limites da conta.",
        "provider_timeout" => "A interação com a IA ultrapassou o tempo permitido.",
        "internal_error" => "O backend interrompeu a execução com um erro interno. Nesta execução antiga, o diagnóstico específico não foi preservado.",
        "tool_failed" => "Uma ferramenta falhou. Consulte o diagnóstico da etapa no relatório.",
        _ => "A execução foi interrompida. O código registrado identifica o motivo para investigação."
    };
    private static JsonElement? Diagnostic(CheckpointEvidence evidence)
    {
        if (evidence.Tool != "test") return null;
        try { return JsonSerializer.Deserialize<JsonElement>(evidence.Detail); }
        catch (JsonException) { return JsonSerializer.SerializeToElement(evidence.Detail); }
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
        group.MapGet("/report", async (Guid organizationId, Guid projectId, Guid taskId, Guid executionId,
            ClaimsPrincipal user, ProgressAccess progress, CancellationToken ct) =>
            Results.Bytes(await progress.ReportAsync(organizationId, projectId, taskId, executionId, null, user, ct), "application/json"));
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
