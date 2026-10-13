using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Memory;
using TeamFlow.Api.Http;
using Xunit;

namespace TeamFlow.Api.Tests;

public sealed class MemoryTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task SourcesAreScopedVersionedAndArchivedWithoutLosingProvenanceAsync()
    {
        using var human = fixture.Factory.CreateClient();
        await human.RegisterAsync();
        var project = await human.CreateProjectAsync();
        var path = $"/api/v1/organizations/{project.OrganizationId}/projects/{project.Id}/memory";
        using var created = await human.SendAsync(HttpMethod.Post, path + "/", new MemoryInput("docs/rounding.md", "Rounding", "Round monetary values to two decimal places."));
        var source = await created.ReadAsync<MemoryDetail>();
        Assert.Equal(MemoryStatus.Candidate, source.Item.Status);
        using var validated = await human.SendAsync(HttpMethod.Put, path + $"/{source.Item.Id}",
            new MemoryUpdate(source.Item.Version, source.Item.Title, source.Content, MemoryStatus.Validated));
        var current = await validated.ReadAsync<MemoryDetail>();
        Assert.NotEqual(source.Item.Version, current.Item.Version);
        using var stale = await human.SendAsync(HttpMethod.Put, path + $"/{source.Item.Id}",
            new MemoryUpdate(source.Item.Version, "Stale", "Stale body", MemoryStatus.Validated));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var historic = await human.GetAsync(path + $"/{source.Item.Id}/versions/{source.Item.Version}");
        historic.EnsureSuccessStatusCode();
        Assert.Contains("two decimal", await historic.Content.ReadAsStringAsync());
        using var foreign = fixture.Factory.CreateClient();
        await foreign.RegisterAsync();
        using var denied = await foreign.GetAsync(path + $"/{source.Item.Id}");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var archive = await human.SendAsync(HttpMethod.Delete, path + $"/{source.Item.Id}?version={current.Item.Version}", new { });
        var archived = await archive.ReadAsync<MemoryDetail>();
        Assert.NotNull(archived.Item.ArchivedAt);
        using var hidden = await human.GetAsync(path + $"/{source.Item.Id}/versions/{source.Item.Version}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using var list = await human.GetAsync(path + "/?page=1&pageSize=20");
        Assert.Empty((await list.ReadAsync<PageResult<MemoryDto>>()).Items);
        using var restored = await human.SendAsync(HttpMethod.Post, path + $"/{source.Item.Id}/restore", new MemoryVersionInput(archived.Item.Version));
        var restoredItem = await restored.ReadAsync<MemoryDetail>();
        Assert.Null(restoredItem.Item.ArchivedAt);
        Assert.Equal(MemoryStatus.Candidate, restoredItem.Item.Status);
        using var scope = fixture.Factory.Services.CreateScope();
        Assert.Equal(4, await scope.ServiceProvider.GetRequiredService<AppDbContext>().MemoryRevisions.CountAsync(r => r.ItemId == source.Item.Id));
    }

    [Theory]
    [InlineData(".env", "safe example")]
    [InlineData("docs/config.md", "password=actual-fixture-password")]
    [InlineData("id.key", "safe example")]
    [InlineData("docs/ssh.md", "-----BEGIN OPENSSH PRIVATE KEY-----")]
    [InlineData("docs/config.json", "{\"password\":\"fixture-password\"}")]
    [InlineData("docs/auth.md", "Bearer fixture-token-not-for-provider")]
    [InlineData("node_modules/library.ts", "safe example")]
    [InlineData("appsettings.json", "safe example")]
    public async Task ExcludedPathsAndCredentialLikeContentAreRejectedAsync(string key, string content)
    {
        using var human = fixture.Factory.CreateClient();
        await human.RegisterAsync();
        var project = await human.CreateProjectAsync();
        using var rejected = await human.SendAsync(HttpMethod.Post,
            $"/api/v1/organizations/{project.OrganizationId}/projects/{project.Id}/memory/", new MemoryInput(key, "Fixture", content));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using var scope = fixture.Factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().MemoryItems.AnyAsync(m => m.ProjectId == project.Id));
    }
}
