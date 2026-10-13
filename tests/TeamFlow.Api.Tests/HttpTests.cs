using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using TeamFlow.Api.Http;
using Xunit;

namespace TeamFlow.Api.Tests;

public sealed class HttpTests
{
    private static WebApplicationFactory<Program> CreateFactory() => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder => builder
            .UseEnvironment("Testing")
            .UseSetting("ConnectionStrings:Database", "Host=127.0.0.1;Port=1;Database=unavailable;Username=test;Password=test;Timeout=1")
            .UseSetting("Server:Url", "http://localhost:5080"));

    [Fact]
    public async Task MissingEndpointReturnsProblemWithCorrelationAsync()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/missing");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("http_404", body.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("traceId").GetString()));
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }

    [Fact]
    public async Task LivenessIsHealthyButReadinessFailsWithoutDatabaseAsync()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var live = await client.GetAsync("/health");
        using var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal("Healthy", await live.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
    }

    [Fact]
    public async Task OpenApiIsNotPublicOutsideDevelopmentAsync()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 101)]
    [InlineData(100001, 20)]
    public void PaginationRejectsRequestsOutsideLimits(int page, int pageSize)
    {
        var error = Assert.Throws<ApiException>(() => Pagination.Validate(page, pageSize));
        Assert.Equal(400, error.Status);
        Assert.Equal("invalid_pagination", error.Code);
    }
}
