namespace TeamFlow.Api.Features.Memory;

public sealed class DemandAnalysis
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid RequestedBy { get; set; }
    public Guid ProfileId { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string State { get; set; } = "calling";
    public string? FailureCode { get; set; }
    public string SourceBundle { get; set; } = "{}";
    public string? Proposal { get; set; }
    public decimal MaxCostUsd { get; set; }
    public decimal ReservedUsd { get; set; }
    public decimal? EstimatedCostUsd { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed record AnalysisInput(string Demand, string? ExpectedBehavior, Guid ProfileId, string IdempotencyKey,
    decimal MaxCostUsd = 1, bool Semantic = false);
public sealed record PlanDraft(string Title, string Description, string[] AcceptanceCriteria, string[] Questions);
public sealed record AnalysisDto(Guid Id, string State, string? FailureCode, PlanDraft? Plan, ContextBundle? Context,
    decimal MaxCostUsd, decimal ReservedUsd, decimal? EstimatedCostUsd, int? InputTokens, int? OutputTokens, DateTimeOffset CreatedAt);
