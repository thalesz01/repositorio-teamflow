namespace TeamFlow.Api.Features.Executions;

public sealed class ModelTurn
{
    public Guid ExecutionId { get; set; }
    public Guid OperationId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ProjectId { get; set; }
    public int Attempt { get; set; }
    public int Sequence { get; set; }
    public string RequestHash { get; set; } = "";
    public string State { get; set; } = "pending";
    public string BillingMode { get; set; } = "api";
    public decimal ReservedUsd { get; set; }
    public decimal? EstimatedCostUsd { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public string? Action { get; set; }
}

public sealed record AgentToolPolicy(string[] Readable, string[] Editable,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string[]? Create = null);
public sealed record ModelTurnInput(Guid LeaseToken, Guid OperationId, int Sequence, AgentToolPolicy Policy);
public sealed record ModelAction(string Tool, string? Path = null, string? Content = null, string? ExpectedHash = null);
public sealed record ModelTurnReply(Guid OperationId, ModelAction Action, decimal? EstimatedCostUsd, int? InputTokens, int? OutputTokens);
