namespace TeamFlow.Api.Features.Executions;

public enum ExecutionState { Queued, Leased, Succeeded, Failed, Cancelled }

public sealed class Execution
{
    public Guid? ParentExecutionId { get; set; }
    public string? ReviewFeedback { get; set; }
    // The skill chosen for this execution, kept as it was read so the run stays reproducible and portable.
    public string? SkillName { get; set; }
    public string? SkillSnapshot { get; set; }
    public string? ReviewState { get; set; }
    public string? ReviewHeadCommit { get; set; }
    public bool ReviewCiPassed { get; set; }
    public Guid? ReviewApprovedBy { get; set; }
    public string? ReviewApprovedHead { get; set; }
    public DateTimeOffset? ReviewCheckedAt { get; set; }
    public string? ConfirmedMergeCommit { get; set; }
    public Guid? MergeRequestedBy { get; set; }
    public DateTimeOffset? MergeRequestedAt { get; set; }
    public string? PublicationState { get; set; }
    public int PublicationAttempts { get; set; }
    public Guid? PublicationLease { get; set; }
    public DateTimeOffset? PublicationLeaseUntil { get; set; }
    public string? PublicationFailureCode { get; set; }
    public long? PullRequestNumber { get; set; }
    public string? PullRequestUrl { get; set; }
    public string? PublishedHeadCommit { get; set; }
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid TaskId { get; set; }
    public Guid RequestedBy { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string TaskSnapshot { get; set; } = "";
    public string? MemoryContext { get; set; }
    public string? TransferSnapshot { get; set; }
    public string? RepositorySnapshot { get; set; }
    public Guid? AgentProfileId { get; set; }
    public ExecutionState State { get; set; } = ExecutionState.Queued;
    public int MaxDurationSeconds { get; set; }
    public int MaxAttempts { get; set; }
    public int MaxSteps { get; set; }
    public decimal MaxCostUsd { get; set; }
    public int Attempt { get; set; }
    public Guid? WorkerId { get; set; }
    public Guid? RunnerCredentialId { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset? DeadlineAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool CancellationRequested { get; set; }
    public string? FailureCode { get; set; }
    public Guid? ResultOperationId { get; set; }
    public string? ResultHash { get; set; }
    public string? ResultCode { get; set; }
    public int? ResultCheckpointSequence { get; set; }
    public DateTimeOffset? ResultCreatedAt { get; set; }
}

public sealed record ExecutionInput(Guid Version, int MaxDurationSeconds = 900, int MaxAttempts = 3, int MaxSteps = 50, decimal MaxCostUsd = 5, Guid? AgentProfileId = null, Guid? ParentExecutionId = null, string? Feedback = null, string? Skill = null);
public sealed record ExecutionDto(Guid Id, Guid TaskId, ExecutionState State, int Attempt, int MaxAttempts,
    int MaxDurationSeconds, int MaxSteps, decimal MaxCostUsd, DateTimeOffset CreatedAt, bool CancellationRequested, string? FailureCode, Guid? ParentExecutionId = null, string? Skill = null);
public sealed record ExecutionLease(Guid ExecutionId, Guid WorkerId, Guid LeaseToken, DateTimeOffset LeaseExpiresAt,
    DateTimeOffset DeadlineAt, int Attempt, string TaskSnapshot, int MaxSteps, decimal MaxCostUsd);
