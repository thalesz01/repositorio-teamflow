using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TeamFlow.Api.Data;
using TeamFlow.Api.Features.Integrations;
using Xunit;

namespace TeamFlow.Api.Tests;

public sealed class WebhookTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public void SignatureMatchesOfficialVectorAndRejectsModifiedOrMalformedInput()
    {
        const string secret = "It's a Secret to Everybody";
        const string signature = "sha256=757107ea0eb2509fc211221cce984b8a37570b6d7586c22c46f4379c8b043e17";
        Assert.True(WebhookSignature.Valid(Encoding.UTF8.GetBytes("Hello, World!"), signature, secret));
        Assert.False(WebhookSignature.Valid(Encoding.UTF8.GetBytes("Hello, World?"), signature, secret));
        Assert.False(WebhookSignature.Valid([], null, secret));
        Assert.False(WebhookSignature.Valid([], "sha256=" + new string('z', 64), secret));
        Assert.False(WebhookSignature.Valid([], signature, "wrong secret"));
    }

    [Fact]
    public async Task SignedDeliveriesAreDurableDeduplicatedAndScopedToActiveInstallationAsync()
    {
        using var human = fixture.Factory.CreateClient();
        await human.RegisterAsync();
        var project = await human.CreateProjectAsync();
        const string secret = "fixture-webhook-secret-unused-externally";
        fixture.Factory.Services.GetRequiredService<IConfiguration>()["GitHub:WebhookSecret"] = secret;
        var connectionId = Guid.NewGuid();
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.GitConnections.Add(new GitConnection
            {
                Id = connectionId,
                OrganizationId = project.OrganizationId,
                ProjectId = project.Id,
                AppId = 2,
                InstallationId = 3,
                RepositoryId = 1,
                Owner = "fixture",
                Repository = "repo",
                BaseBranch = "main",
                BaseCommit = new string('a', 40),
                ProtectedPrivateKey = "unused-fixture",
                Version = Guid.NewGuid()
            });
            await db.SaveChangesAsync();
        }
        var body = JsonSerializer.SerializeToUtf8Bytes(new { repository = new { id = 1 }, installation = new { id = 3 }, action = "closed", comment = "ação unicode" });
        var delivery = Guid.NewGuid();
        using var incoming = fixture.Factory.CreateClient(); // No human cookie or CSRF: the raw-body signature authorizes this endpoint.
        using var first = await SendAsync(incoming, body, delivery, secret);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var retries = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => SendAsync(incoming, body, delivery, secret)));
        foreach (var response in retries) { Assert.Equal(HttpStatusCode.Accepted, response.StatusCode); response.Dispose(); }
        using var forged = await SendAsync(incoming, body, Guid.NewGuid(), "wrong-fixture-secret");
        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
        var changed = JsonSerializer.SerializeToUtf8Bytes(new { repository = new { id = 1 }, installation = new { id = 3 }, action = "opened" });
        using var conflict = await SendAsync(incoming, changed, delivery, secret);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var foreignBody = JsonSerializer.SerializeToUtf8Bytes(new { repository = new { id = 1 }, installation = new { id = 999 } });
        using var foreign = await SendAsync(incoming, foreignBody, Guid.NewGuid(), secret);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = Assert.Single(await db.GitHubDeliveries.Where(d => d.ConnectionId == connectionId).ToListAsync());
            Assert.Equal(project.OrganizationId, stored.OrganizationId);
            Assert.Equal(project.Id, stored.ProjectId);
            Assert.Equal("pending", stored.State);
            Assert.DoesNotContain(secret, stored.Payload);
            await db.GitConnections.Where(g => g.Id == connectionId).ExecuteUpdateAsync(s => s.SetProperty(g => g.RevokedAt, DateTimeOffset.UtcNow));
        }
        using var revoked = await SendAsync(incoming, body, Guid.NewGuid(), secret);
        Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
        using var oversized = await SendAsync(incoming, new byte[128 * 1024 + 1], Guid.NewGuid(), secret);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, byte[] body, Guid id, string secret)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/github") { Content = new ByteArrayContent(body) };
        request.Headers.Add("X-Hub-Signature-256", "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body)));
        request.Headers.Add("X-GitHub-Event", "pull_request");
        request.Headers.Add("X-GitHub-Delivery", id.ToString("D"));
        return await client.SendAsync(request);
    }
}
