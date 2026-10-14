namespace TeamFlow.Api.Features.Organizations;

public sealed record OrganizationDto(Guid Id, string Name, string Role);
public sealed record OrganizationInput(string Name);
public sealed record MemberDto(Guid UserId, string Email, string Role);
public sealed record MemberInput(string Email);
