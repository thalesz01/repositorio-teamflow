namespace TeamFlow.Api.Features.Projects;

public sealed record ProjectDto(Guid Id, Guid OrganizationId, string Name, Guid Version)
{
    public static ProjectDto From(Project project) => new(project.Id, project.OrganizationId, project.Name, project.Version);
}
public sealed record ProjectInput(string Name);
public sealed record ProjectUpdate(string Name, Guid Version);
