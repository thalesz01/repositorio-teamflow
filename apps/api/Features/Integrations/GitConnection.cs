namespace TeamFlow.Api.Features.Integrations;

public sealed class GitConnection
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ProjectId { get; set; }
    public string Provider { get; set; } = "local";
    public string LocalPath { get; set; } = "";
    public long RepositoryId { get; set; }
    public string BaseBranch { get; set; } = "";
    public string BaseCommit { get; set; } = "";
    public Guid Version { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed record GitDto(Guid Id, string Provider, long RepositoryId, string RemoteUrl, string BaseBranch, string BaseCommit, Guid Version, DateTimeOffset? RevokedAt);
public sealed record RepositorySnapshot(Guid ConnectionId, Guid ConnectionVersion, long RepositoryId, string RemoteUrl, string BaseBranch, string BaseCommit, string Branch, string? LocalPath = null);
// The runner only accepts the literal "local" grant: checkout reads the authorized folder, no credential exists.
public sealed record CheckoutGrant(string Token, DateTimeOffset ExpiresAt);
