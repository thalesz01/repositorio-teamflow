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
            && t.OrganizationId == organizationId && t.ProjectId == projectId && t.State != ModelTurnState.Completed)
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
