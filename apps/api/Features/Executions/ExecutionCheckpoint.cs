using System.Text.Json;
using System.Text.Json.Serialization;
namespace TeamFlow.Api.Features.Executions;

public sealed class ExecutionCheckpoint
{
    public Guid ExecutionId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ProjectId { get; set; }
    public int Attempt { get; set; }
    public int Sequence { get; set; }
    public string Context { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

// API-owned wire types; the runner owns its independent consumer types.
public sealed record CheckpointEvidence(int Step, string Tool, string? Path, bool Succeeded, string Detail, string? Hash = null);
public sealed record CheckpointContext(int SchemaVersion, Guid ExecutionId, string Objective, string[] AcceptanceCriteria,
    string BaseCommit, CheckpointEvidence[] Evidence, string[] Pending,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Memory = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? History = null);
public sealed record CheckpointInput(Guid LeaseToken, CheckpointContext Context);
public sealed record CheckpointReadInput(Guid LeaseToken);
public sealed record CheckpointReply(int Attempt, int Sequence, string ContentHash, DateTimeOffset CreatedAt, CheckpointContext Context);
