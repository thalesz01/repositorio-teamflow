using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TeamFlow.Api.Http;
using Xunit;

namespace TeamFlow.Api.Tests;

public sealed class ErrorTests
{
    [Fact]
    public async Task UnexpectedErrorDoesNotExposeItsDetailsAsync()
    {
        using var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Headers.Accept = "application/json";
        using var stream = new MemoryStream();
        context.Response.Body = stream;
        var handler = new ApiExceptionHandler(services.GetRequiredService<IProblemDetailsService>(), NullLogger<ApiExceptionHandler>.Instance);

        Assert.True(await handler.TryHandleAsync(context, new InvalidOperationException("private credential"), CancellationToken.None));
        stream.Position = 0;
        using var body = await JsonDocument.ParseAsync(stream);
        Assert.Equal(500, context.Response.StatusCode);
        Assert.Equal("internal_error", body.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("private credential", body.RootElement.ToString());
    }
}
