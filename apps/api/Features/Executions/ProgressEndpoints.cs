using System.Security.Claims;
using System.Text.Json;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Executions;

public static class ProgressEndpoints
{
    public static void MapProgress(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/organizations/{organizationId:guid}/projects/{projectId:guid}/tasks/{taskId:guid}/executions/{executionId:guid}")
            .WithTags("Executions").RequireAuthorization();
        group.MapGet("/progress", ReadAsync).Produces<ExecutionProgress>();
        group.MapGet("/report", ReportAsync);
        group.MapGet("/reports/{eventId}", EventReportAsync);
        group.MapGet("/events", StreamAsync).Produces(StatusCodes.Status200OK, contentType: "text/event-stream");
    }

    private static async Task<ExecutionProgress> ReadAsync(Guid organizationId, Guid projectId, Guid taskId, Guid executionId, string? after,
        ClaimsPrincipal user, ProgressAccess progress, CancellationToken ct) =>
        await progress.ReadAsync(organizationId, projectId, taskId, executionId, after, user, ct);

    private static async Task<IResult> ReportAsync(Guid organizationId, Guid projectId, Guid taskId, Guid executionId,
        ClaimsPrincipal user, ProgressAccess progress, CancellationToken ct) =>
        Results.Bytes(await progress.ReportAsync(organizationId, projectId, taskId, executionId, null, user, ct), "application/json");

    private static async Task<IResult> EventReportAsync(Guid organizationId, Guid projectId, Guid taskId, Guid executionId,
        string eventId, ClaimsPrincipal user, ProgressAccess progress, CancellationToken ct) =>
        Results.File(await progress.ReportAsync(organizationId, projectId, taskId, executionId, eventId, user, ct),
            "application/json", $"execution-{executionId:N}-{eventId}.json");

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
