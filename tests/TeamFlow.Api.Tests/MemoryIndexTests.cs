using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Integrations;
using TeamFlow.Api.Features.Memory;
using Xunit;

namespace TeamFlow.Api.Tests;

public sealed class MemoryIndexTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task TextIndexIsIdempotentRebuildableScopedAndStopsWhenTheProfileIsRevokedAsync()
    {
        using var human = fixture.Factory.CreateClient();
        await human.RegisterAsync();
        var project = await human.CreateProjectAsync();
        var path = $"/api/v1/organizations/{project.OrganizationId}/projects/{project.Id}";
        var profile = await fixture.Factory.Services.SeedProfileAsync(project);
        using var sourceResponse = await human.SendAsync(HttpMethod.Post, path + "/memory/", new MemoryInput("docs/calc.md", "Calculation", string.Concat(Enumerable.Repeat("Arredondamento correto. 🧪 ", 200))));
        var candidate = await sourceResponse.ReadAsync<MemoryDetail>();
        using var invalid = await human.SendAsync(HttpMethod.Post, path + $"/memory/{candidate.Item.Id}/indexes", new IndexInput(candidate.Item.Version, profile.Id, "candidate-index", 1));
        Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
        using var validatedResponse = await human.SendAsync(HttpMethod.Put, path + $"/memory/{candidate.Item.Id}", new MemoryUpdate(candidate.Item.Version, candidate.Item.Title, candidate.Content, MemoryStatus.Validated));
        var source = await validatedResponse.ReadAsync<MemoryDetail>();
        var indexPath = path + $"/memory/{source.Item.Id}/indexes";
        var request = new IndexInput(source.Item.Version, profile.Id, "index-original", 1);
        using var queuedResponse = await human.SendAsync(HttpMethod.Post, indexPath, request);
        var queued = await queuedResponse.ReadAsync<IndexDto>();
        Assert.Equal("text-only", queued.Model);
        Assert.Equal(0, queued.Dimensions);
        using var replayResponse = await human.SendAsync(HttpMethod.Post, indexPath, request);
        Assert.Equal(queued.Id, (await replayResponse.ReadAsync<IndexDto>()).Id);
        using var conflicting = await human.SendAsync(HttpMethod.Post, indexPath, request with { MaxCostUsd = 2 });
        Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);
        using var outsider = fixture.Factory.CreateClient(); await outsider.RegisterAsync();
        using var denied = await outsider.GetAsync(indexPath);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        async Task ProcessAsync()
        { using var scope = fixture.Factory.Services.CreateScope(); Assert.True(await scope.ServiceProvider.GetRequiredService<MemoryIndexAccess>().ProcessAsync(default)); }
        await ProcessAsync();
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal("completed", (await db.MemoryIndexes.SingleAsync(i => i.Id == queued.Id)).State);
            var chunks = await db.MemoryChunks.Where(c => c.IndexId == queued.Id).OrderBy(c => c.Sequence).ToListAsync();
            Assert.True(chunks.Count > 1); Assert.Equal(source.Content, string.Concat(chunks.Select(c => c.Content)));
            Assert.All(chunks, c => Assert.InRange(Encoding.UTF8.GetByteCount(c.Content), 1, 2048));
            Assert.All(chunks, c => Assert.Empty(c.Embedding));
        }
        using var rebuild = await human.SendAsync(HttpMethod.Post, indexPath, request with { IdempotencyKey = "index-rebuild" });
        rebuild.EnsureSuccessStatusCode();
        await ProcessAsync();
        using var searchResponse = await human.SendAsync(HttpMethod.Post, path + "/memory/search", new SearchInput("arredondamento correto"));
        Assert.Single((await searchResponse.ReadAsync<ContextBundle>()).Sources.Select(s => s.ItemId).Distinct());
        using var revokedQueue = await human.SendAsync(HttpMethod.Post, indexPath, request with { IdempotencyKey = "index-revoked" });
        var revoked = await revokedQueue.ReadAsync<IndexDto>();
        using var revoke = await human.SendAsync(HttpMethod.Delete, path + $"/profiles/{profile.Id}", new { }); revoke.EnsureSuccessStatusCode();
        await ProcessAsync();
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var failed = await scope.ServiceProvider.GetRequiredService<AppDbContext>().MemoryIndexes.SingleAsync(i => i.Id == revoked.Id);
            Assert.Equal("failed", failed.State);
            Assert.Equal("profile_not_found", failed.FailureCode);
        }
    }
}
