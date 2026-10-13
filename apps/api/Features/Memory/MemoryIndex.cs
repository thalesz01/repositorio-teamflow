namespace TeamFlow.Api.Features.Memory;

public sealed class MemoryIndex
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid ItemId { get; set; }
    public Guid SourceVersion { get; set; }
    public Guid ProfileId { get; set; }
    public Guid RequestedBy { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string Model { get; set; } = "text-embedding-3-small";
    public int Dimensions { get; set; } = 1536;
    public string Chunker { get; set; } = "utf8-2048-v1";
    public string State { get; set; } = "queued";
    public string? FailureCode { get; set; }
    public Guid? Lease { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public decimal MaxCostUsd { get; set; }
    public decimal ReservedUsd { get; set; }
    public decimal? EstimatedCostUsd { get; set; }
    public int? InputTokens { get; set; }
}

public sealed class MemoryChunk
{
    public Guid IndexId { get; set; }
    public int Sequence { get; set; }
    public string Content { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public float[] Embedding { get; set; } = [];
}

public sealed record IndexInput(Guid Version, Guid ProfileId, string IdempotencyKey, decimal MaxCostUsd);
public sealed record IndexDto(Guid Id, Guid ItemId, Guid SourceVersion, string Model, int Dimensions, string Chunker,
    string State, string? FailureCode, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt,
    decimal MaxCostUsd, decimal ReservedUsd, decimal? EstimatedCostUsd, int? InputTokens);
