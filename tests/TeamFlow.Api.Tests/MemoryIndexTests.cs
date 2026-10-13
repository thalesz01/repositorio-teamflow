using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Integrations;
using TeamFlow.Api.Features.Memory;
using TeamFlow.Api.Http;
using Xunit;

namespace TeamFlow.Api.Tests;

public sealed class MemoryIndexTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task IndexIsIdempotentRebuildableScopedAndUncertainCallsAreNotRepeatedAsync()
    {
        var embedding = new FixtureEmbedding();
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<IEmbeddingModel>(embedding)));
        using var human = factory.CreateClient();
        await human.RegisterAsync();
        var project = await human.CreateProjectAsync();
        var path = $"/api/v1/organizations/{project.OrganizationId}/projects/{project.Id}";
        var config = factory.Services.GetRequiredService<IConfiguration>();
        config[$"Integrations:{project.OrganizationId}:{project.Id}:OpenAI:ApiKey"] = "fixture-key-unused-by-double";
        config["OpenAI:EnableRequests"] = "true";
        config["Memory:EnableEmbeddingRequests"] = "true";
        config["Memory:EmbeddingUsdPerMillionTokens"] = "1";
        using var profileResponse = await human.SendAsync(HttpMethod.Post, path + "/profiles/", new ProfileInput("Fixture", "fixture-model", 4096, 128, 5, 1, 2));
        var profile = await profileResponse.ReadAsync<ProfileDto>();
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
        using var replayResponse = await human.SendAsync(HttpMethod.Post, indexPath, request);
        Assert.Equal(queued.Id, (await replayResponse.ReadAsync<IndexDto>()).Id);
        using var outsider = factory.CreateClient(); await outsider.RegisterAsync();
        using var denied = await outsider.GetAsync(indexPath);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        async Task ProcessAsync()
        { using var scope = factory.Services.CreateScope(); Assert.True(await scope.ServiceProvider.GetRequiredService<MemoryIndexAccess>().ProcessAsync(default)); }
        await ProcessAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal("completed", (await db.MemoryIndexes.SingleAsync(i => i.Id == queued.Id)).State);
            var chunks = await db.MemoryChunks.Where(c => c.IndexId == queued.Id).OrderBy(c => c.Sequence).ToListAsync();
            Assert.True(chunks.Count > 1); Assert.Equal(source.Content, string.Concat(chunks.Select(c => c.Content)));
            Assert.All(chunks, c => Assert.InRange(Encoding.UTF8.GetByteCount(c.Content), 1, 2048));
        }
        using var rebuild = await human.SendAsync(HttpMethod.Post, indexPath, request with { IdempotencyKey = "index-rebuild" });
        rebuild.EnsureSuccessStatusCode();
        await ProcessAsync(); Assert.Equal(2, embedding.Calls);
        embedding.Fail = true;
        using var uncertainResponse = await human.SendAsync(HttpMethod.Post, indexPath, request with { IdempotencyKey = "index-uncertain" });
        var uncertain = await uncertainResponse.ReadAsync<IndexDto>();
        await ProcessAsync();
        using (var scope = factory.Services.CreateScope())
        {
            Assert.Equal("uncertain", (await scope.ServiceProvider.GetRequiredService<AppDbContext>().MemoryIndexes.SingleAsync(i => i.Id == uncertain.Id)).State);
            Assert.False(await scope.ServiceProvider.GetRequiredService<MemoryIndexAccess>().ProcessAsync(default));
        }
        Assert.Equal(3, embedding.Calls);
        using var revokedQueue = await human.SendAsync(HttpMethod.Post, indexPath, request with { IdempotencyKey = "index-revoked" });
        var revoked = await revokedQueue.ReadAsync<IndexDto>();
        using var revoke = await human.SendAsync(HttpMethod.Delete, path + $"/profiles/{profile.Id}", new { }); revoke.EnsureSuccessStatusCode();
        await ProcessAsync(); Assert.Equal(3, embedding.Calls);
        using (var scope = factory.Services.CreateScope())
            Assert.Equal("failed", (await scope.ServiceProvider.GetRequiredService<AppDbContext>().MemoryIndexes.SingleAsync(i => i.Id == revoked.Id)).State);
    }

    private sealed class FixtureEmbedding : IEmbeddingModel
    {
        public int Calls { get; private set; }
        public bool Fail { get; set; }
        public Task<EmbeddingResult> EmbedAsync(string key, string[] inputs, decimal price, CancellationToken ct)
        {
            Calls++;
            if (Fail) throw new HttpRequestException("Simulated response lost after provider processing.");
            return Task.FromResult(new EmbeddingResult(inputs.Select(_ => Enumerable.Repeat(0.1f, 1536).ToArray()).ToArray(), 100, 0.0001m));
        }
    }
}
