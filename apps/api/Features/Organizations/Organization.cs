namespace TeamFlow.Api.Features.Organizations;

public sealed class Organization
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class Membership
{
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = "member";
}
