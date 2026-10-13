namespace TeamFlow.Api.Features.Integrations;

public sealed class GitConnection
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ProjectId { get; set; }
    public string Provider { get; set; } = "github";
    public string LocalPath { get; set; } = "";
    public long AppId { get; set; }
    public long InstallationId { get; set; }
    public long RepositoryId { get; set; }
    public string Owner { get; set; } = "";
    public string Repository { get; set; } = "";
    public string BaseBranch { get; set; } = "";
    public string BaseCommit { get; set; } = "";
    public string ProtectedPrivateKey { get; set; } = "";
    public Guid Version { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed record GitInput(long InstallationId, long RepositoryId, string Owner, string Repository, string BaseBranch);
public sealed record GitDto(Guid Id, string Provider, long RepositoryId, string RemoteUrl, string BaseBranch, string BaseCommit, Guid Version, DateTimeOffset? RevokedAt);
public sealed record RepositorySnapshot(Guid ConnectionId, Guid ConnectionVersion, long RepositoryId, string RemoteUrl, string BaseBranch, string BaseCommit, string Branch, string? LocalPath = null);
public sealed record InstallationToken(string Token, DateTimeOffset ExpiresAt)
{
    public override string ToString() => "InstallationToken [redacted]";
}
