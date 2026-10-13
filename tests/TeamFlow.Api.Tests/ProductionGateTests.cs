using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace TeamFlow.Api.Tests;

public sealed class ProductionGateTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public void ProductionCannotBeActivatedByEnablingExternalCalls()
    {
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production")
            .UseSetting("OpenAI:EnableRequests", "true").UseSetting("GitHub:EnablePublication", "true"));
        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("Production deployment is blocked", error.ToString());
    }
}
