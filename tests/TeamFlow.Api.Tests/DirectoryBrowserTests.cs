using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TeamFlow.Api.Features.Integrations;
using Xunit;

namespace TeamFlow.Api.Tests;

public sealed class DirectoryBrowserTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task BrowseIsScopedRejectsEscapesAndDoesNotReadFilesAsync()
    {
        using var client = fixture.Factory.CreateClient();
        using var outsider = fixture.Factory.CreateClient();
        await client.RegisterAsync(); await outsider.RegisterAsync();
        var project = await client.CreateProjectAsync();
        var endpoint = $"/api/v1/organizations/{project.OrganizationId}/projects/{project.Id}/repository/local/directories";
        var root = Path.Combine(Path.GetTempPath(), "teamflow-browser-" + Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(root, "repository");
        Directory.CreateDirectory(Path.Combine(repository, ".git"));
        Directory.CreateDirectory(Path.Combine(root, "other-folder"));
        await File.WriteAllTextAsync(Path.Combine(root, "secret.txt"), "never returned");
        try
        {
            var configuration = fixture.Factory.Services.GetRequiredService<IConfiguration>();
            var prefix = $"Integrations:{project.OrganizationId:D}:{project.Id:D}:Local:";
            configuration[prefix + "RepositoryPath"] = repository;
            configuration[prefix + "BrowseRoots:0"] = root;
            var otherRepository = Path.Combine(root, "other-folder");
            Directory.CreateDirectory(Path.Combine(otherRepository, ".git"));
            using (var scope = fixture.Factory.Services.CreateScope())
            {
                var local = scope.ServiceProvider.GetRequiredService<LocalRepository>();
                Assert.Throws<TeamFlow.Api.Http.ApiException>(() => local.AuthorizedPath(project.OrganizationId, project.Id, otherRepository));
                configuration[prefix + "AllowRepositorySelection"] = "true";
                Assert.Equal(otherRepository, local.AuthorizedPath(project.OrganizationId, project.Id, otherRepository));
                Assert.Throws<TeamFlow.Api.Http.ApiException>(() => local.AuthorizedPath(project.OrganizationId, project.Id, root + "-sibling"));
                configuration[prefix + "AllowRepositorySelection"] = "false";
            }
            using var foreign = await outsider.GetAsync(endpoint);
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
            using var rootsResponse = await client.GetAsync(endpoint);
            var roots = await rootsResponse.ReadAsync<LocalDirectoryListing>();
            Assert.Equal([root], roots.Roots); Assert.Null(roots.CurrentPath);
            using var response = await client.GetAsync(endpoint + "?path=" + Uri.EscapeDataString(root));
            var listing = await response.ReadAsync<LocalDirectoryListing>();
            Assert.Equal(2, listing.Directories.Length); Assert.Null(listing.ParentPath); Assert.False(listing.CanConnect);
            Assert.DoesNotContain("secret", await response.Content.ReadAsStringAsync());
            using var repoResponse = await client.GetAsync(endpoint + "?path=" + Uri.EscapeDataString(repository));
            var repo = await repoResponse.ReadAsync<LocalDirectoryListing>();
            Assert.True(repo.IsRepository); Assert.True(repo.CanConnect); Assert.Equal(root, repo.ParentPath); Assert.Empty(repo.Directories);
            foreach (var escape in new[] { Path.Combine(root, ".."), root + "-sibling" })
            {
                using var denied = await client.GetAsync(endpoint + "?path=" + Uri.EscapeDataString(escape));
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            }
            using var relative = await client.GetAsync(endpoint + "?path=relative");
            Assert.Equal(HttpStatusCode.BadRequest, relative.StatusCode);
            configuration[prefix + "BrowseAllDrives"] = "true";
            configuration[prefix + "AllowRepositorySelection"] = "true";
            using var drivesResponse = await client.GetAsync(endpoint);
            Assert.NotEmpty((await drivesResponse.ReadAsync<LocalDirectoryListing>()).Roots);
            using var selectableResponse = await client.GetAsync(endpoint + "?path=" + Uri.EscapeDataString(otherRepository));
            Assert.True((await selectableResponse.ReadAsync<LocalDirectoryListing>()).CanConnect);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
