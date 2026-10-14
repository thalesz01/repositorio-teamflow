using System.Linq.Expressions;

namespace TeamFlow.Api.Features.Executions;

// A turn stays pending while the provider runs; an interrupted one becomes uncertain and is never retried automatically.
public enum ModelTurnState { Pending, Completed, Uncertain }

// Api turns are billed per token and need a known cost; cli turns use the signed-in CLI and have no tariff.
public enum BillingMode { Api, Cli }

public sealed class ModelTurn
{
    public Guid ExecutionId { get; set; }
    public Guid OperationId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ProjectId { get; set; }
    public int Attempt { get; set; }
    public int Sequence { get; set; }
    public string RequestHash { get; set; } = "";
    public ModelTurnState State { get; set; } = ModelTurnState.Pending;
    public BillingMode BillingMode { get; set; } = BillingMode.Api;
    public decimal ReservedUsd { get; set; }
    public decimal? EstimatedCostUsd { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    // The part of InputTokens the provider served from its prompt cache; null when the CLI did not say.
    public int? CachedInputTokens { get; set; }
    public string? Action { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureType { get; set; }

    // A turn whose outcome or cost is unknown (pending, interrupted, or metered without a reported cost) blocks further
    // model calls and result validation: it is never retried automatically and never counted as zero.
    public static readonly Expression<Func<ModelTurn, bool>> Unresolved = t =>
        t.State != ModelTurnState.Completed || (t.BillingMode == BillingMode.Api && t.EstimatedCostUsd == null);
    private static readonly Func<ModelTurn, bool> UnresolvedCheck = Unresolved.Compile();
    public bool IsUnresolved => UnresolvedCheck(this);
}

public sealed record AgentToolPolicy(string[] Readable, string[] Editable,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string[]? Create = null);
public sealed record ModelTurnInput(Guid LeaseToken, Guid OperationId, int Sequence, AgentToolPolicy Policy);
public sealed record ModelAction(string Tool, string? Path = null, string? Content = null, string? ExpectedHash = null);
public sealed record ModelTurnReply(Guid OperationId, ModelAction Action, decimal? EstimatedCostUsd, int? InputTokens, int? OutputTokens);
