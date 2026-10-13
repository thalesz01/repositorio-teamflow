namespace TeamFlow.Api.Features.Memory;

public enum MemoryKind { Document, Code, Decision, Resolution }
public enum MemoryStatus { Candidate, Validated, Obsolete }

public sealed class MemoryItem
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ProjectId { get; set; }
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public MemoryKind Kind { get; set; }
    public MemoryStatus Status { get; set; }
    public Guid Version { get; set; }
    public string ContentHash { get; set; } = "";
    public Guid CreatedBy { get; set; }
    public Guid UpdatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? ExecutionId { get; set; }
    public string? PullRequestUrl { get; set; }
    public string? Commit { get; set; }
}

public sealed class MemoryRevision
{
    public Guid ItemId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid Version { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public MemoryStatus Status { get; set; }
    public bool Archived { get; set; }
    public Guid AuthorId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed record MemoryInput(string Key, string Title, string Content, MemoryKind Kind = MemoryKind.Document);
public sealed record MemoryUpdate(Guid Version, string Title, string Content, MemoryStatus Status);
public sealed record MemoryVersionInput(Guid Version);
public sealed record MemoryDto(Guid Id, string Key, string Title, MemoryKind Kind, MemoryStatus Status, Guid Version,
    string ContentHash, Guid CreatedBy, Guid UpdatedBy, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt, Guid? TaskId, Guid? ExecutionId, string? PullRequestUrl, string? Commit);
public sealed record MemoryDetail(MemoryDto Item, string Content);

public sealed record MemoryVersionDto(Guid Version, string Title, string Content, string ContentHash, MemoryStatus Status,
    bool Archived, Guid AuthorId, DateTimeOffset CreatedAt);
