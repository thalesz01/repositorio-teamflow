namespace TeamFlow.Api.Features.Organizations;

public enum MemberRole { Member, Owner }

public sealed class Organization
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class Membership
{
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public MemberRole Role { get; set; } = MemberRole.Member;
}
