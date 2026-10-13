namespace TeamFlow.Api.Features.Runners;

public sealed class RunnerCredential
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid WorkerId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed record RunnerInput(int ValidityDays = 7);
public sealed record RunnerDto(Guid Id, Guid WorkerId, DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt);
public sealed record IssuedRunner(RunnerDto Credential, string Token);
public sealed record ClaimInput(int LeaseSeconds = 30);
public sealed record HeartbeatInput(Guid LeaseToken, int LeaseSeconds = 30);

public sealed record RunnerLimits(int MaxDurationSeconds, int MaxAttempts, int MaxSteps, decimal MaxCostUsd);
public sealed record RunnerMessage(int SchemaVersion, Guid ExecutionId, Guid TaskId, Guid OrganizationId, Guid ProjectId,
    Guid WorkerId, Guid LeaseToken, DateTimeOffset LeaseExpiresAt, DateTimeOffset DeadlineAt, int Attempt,
    System.Text.Json.JsonElement Snapshot, RunnerLimits Limits, object? Repository, Guid? AgentProfileId, System.Text.Json.JsonElement? Memory = null, System.Text.Json.JsonElement? Transfer = null);
public sealed record HeartbeatReply(bool ContinueExecution, string? Code = null);
