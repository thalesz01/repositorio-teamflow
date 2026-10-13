namespace TeamFlow.Api.Features.Integrations;

public sealed class AgentProfile
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ProjectId { get; set; }
    public string Provider { get; set; } = "openai";
    public string Name { get; set; } = "";
    public string Model { get; set; } = "";
    public string ProtectedApiKey { get; set; } = "";
    public int MaxInputBytes { get; set; }
    public int MaxOutputTokens { get; set; }
    public int TimeoutSeconds { get; set; }
    public decimal InputUsdPerMillionTokens { get; set; }
    public decimal OutputUsdPerMillionTokens { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed record ProfileInput(string Name, string Model, int MaxInputBytes, int MaxOutputTokens, int TimeoutSeconds,
    decimal InputUsdPerMillionTokens, decimal OutputUsdPerMillionTokens);
public sealed record ProfileDto(Guid Id, string Name, string Provider, string Model, string[] Capabilities, string[] Tools,
    int MaxInputBytes, int MaxOutputTokens, int TimeoutSeconds, decimal InputUsdPerMillionTokens,
    decimal OutputUsdPerMillionTokens, DateTimeOffset? RevokedAt);
