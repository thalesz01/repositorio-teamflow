using System.Net;
using System.Net.Http.Json;
using TeamFlow.Api.Features.Auth;
using TeamFlow.Api.Features.Organizations;
using TeamFlow.Api.Features.Projects;
using Xunit;

namespace TeamFlow.Api.Tests;

public sealed class OrganizationTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task TeamsAreIsolatedAndOnlyOwnerManagesProjectsAsync()
    {
        using var owner = fixture.Factory.CreateClient();
        using var member = fixture.Factory.CreateClient();
        using var outsider = fixture.Factory.CreateClient();
        await owner.RegisterAsync();
        var memberAccount = await member.RegisterAsync();
        await outsider.RegisterAsync();
        using var organizationResponse = await owner.SendAsync(HttpMethod.Post, "/api/v1/organizations/", new OrganizationInput("Team A"));
        var organization = await organizationResponse.ReadAsync<OrganizationDto>();
        var projectsPath = $"/api/v1/organizations/{organization.Id}/projects";
        using var projectResponse = await owner.SendAsync(HttpMethod.Post, projectsPath + "/", new ProjectInput("API"));
        var project = await projectResponse.ReadAsync<ProjectDto>();
        using var foreignOrganizationResponse = await outsider.SendAsync(HttpMethod.Post, "/api/v1/organizations/", new OrganizationInput("Team B"));
        var foreignOrganization = await foreignOrganizationResponse.ReadAsync<OrganizationDto>();

        using var foreignRead = await outsider.GetAsync(projectsPath + $"/{project.Id}");
        Assert.Equal(HttpStatusCode.NotFound, foreignRead.StatusCode);
        using var forgedScope = await owner.GetAsync($"/api/v1/organizations/{organization.Id}/projects/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, forgedScope.StatusCode);
        using var crossProject = await outsider.GetAsync($"/api/v1/organizations/{foreignOrganization.Id}/projects/{project.Id}");
        Assert.Equal(HttpStatusCode.NotFound, crossProject.StatusCode);
        using var foreignWrite = await outsider.SendAsync(HttpMethod.Put, projectsPath + $"/{project.Id}", new ProjectUpdate("stolen", project.Version));
        Assert.Equal(HttpStatusCode.NotFound, foreignWrite.StatusCode);

        using var addition = await owner.SendAsync(HttpMethod.Post, $"/api/v1/organizations/{organization.Id}/members", new MemberInput(memberAccount.Email));
        Assert.Equal(HttpStatusCode.Created, addition.StatusCode);
        using var memberRead = await member.GetAsync(projectsPath + $"/{project.Id}");
        Assert.Equal(HttpStatusCode.OK, memberRead.StatusCode);
        using var memberWrite = await member.SendAsync(HttpMethod.Post, projectsPath + "/", new ProjectInput("forbidden"));
        Assert.Equal(HttpStatusCode.Forbidden, memberWrite.StatusCode);

        using var update = await owner.SendAsync(HttpMethod.Put, projectsPath + $"/{project.Id}", new ProjectUpdate("Renamed", project.Version));
        var renamed = await update.ReadAsync<ProjectDto>();
        using var staleUpdate = await owner.SendAsync(HttpMethod.Put, projectsPath + $"/{project.Id}", new ProjectUpdate("Stale", project.Version));
        Assert.Equal(HttpStatusCode.Conflict, staleUpdate.StatusCode);
        using var removal = await owner.SendAsync(HttpMethod.Delete, $"/api/v1/organizations/{organization.Id}/members/{memberAccount.UserId}", new { });
        Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);
        using var revokedRead = await member.GetAsync(projectsPath + $"/{project.Id}");
        Assert.Equal(HttpStatusCode.NotFound, revokedRead.StatusCode);
        using var delete = await owner.SendAsync(HttpMethod.Delete, projectsPath + $"/{project.Id}?version={renamed.Version}", new { });
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [Fact]
    public async Task RegistrationRequiresCsrfAndLogoutEndsTheSessionAsync()
    {
        using var client = fixture.Factory.CreateClient();
        using var invalid = await client.PostAsJsonAsync("/api/v1/auth/register", new Credentials("no-csrf@example.test", "Local-Test-Password42!"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        await client.RegisterAsync();
        using var session = await client.GetAsync("/api/v1/auth/session");
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        using var logout = await client.SendAsync(HttpMethod.Post, "/api/v1/auth/logout", new { });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var afterLogout = await client.GetAsync("/api/v1/auth/session");
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }
}
