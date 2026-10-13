using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Tasks;
using TeamFlow.Api.Http;

namespace TeamFlow.Api.Features.Executions;

public sealed record MergedKnowledge(Guid ExecutionId, Guid TaskId, Guid OrganizationId, Guid ProjectId,
    Guid AuthorId, string Title, string Content, string PullRequestUrl, string Commit);

public sealed class ExecutionKnowledge(AppDbContext db)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) } };

    public async Task<MergedKnowledge> ReadAsync(Guid organizationId, Guid projectId, Guid id, CancellationToken ct)
    {
        var execution = await db.Executions.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id && e.OrganizationId == organizationId
            && e.ProjectId == projectId && e.ReviewState == "merged" && e.ConfirmedMergeCommit != null, ct)
            ?? throw new ApiException(404, "merged_resolution_not_found", "Verified merged execution not found.");
        var task = JsonSerializer.Deserialize<TaskDto>(execution.TaskSnapshot, JsonOptions)
            ?? throw new InvalidDataException("Invalid task snapshot.");
        var evidence = await db.ExecutionCheckpoints.AsNoTracking().Where(c => c.ExecutionId == id && c.OrganizationId == organizationId
            && c.ProjectId == projectId && c.Attempt == execution.Attempt && c.Sequence == execution.ResultCheckpointSequence)
            .Select(c => c.ContentHash).FirstOrDefaultAsync(ct);
        var text = "Candidate resolution; validate with the team before treating it as guidance.\nProblem: " + task.Description
            + "\nAcceptance criteria:\n" + string.Join("\n", (task.AcceptanceCriteria ?? []).Select(c => "- " + c))
            + $"\nController result: {execution.ResultCode}\nCheckpoint SHA-256: {evidence ?? "unavailable"}"
            + $"\nPR: {execution.PullRequestUrl}\nHead: {execution.PublishedHeadCommit}\nConfirmed merge: {execution.ConfirmedMergeCommit}"
            + "\nThese references establish provenance; they do not automatically validate business or architecture claims.";
        return new(id, execution.TaskId, organizationId, projectId, execution.ReviewApprovedBy ?? execution.RequestedBy,
            task.Title ?? "Resolution " + id.ToString("N")[..12], text, execution.PullRequestUrl ?? throw new InvalidDataException("Missing PR receipt."),
            execution.ConfirmedMergeCommit ?? throw new InvalidDataException("Missing merge receipt."));
    }
}
