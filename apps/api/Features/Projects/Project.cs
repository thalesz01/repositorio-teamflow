namespace TeamFlow.Api.Features.Projects;

public sealed class Project
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = "";
    public Guid Version { get; set; } = Guid.NewGuid();
}
