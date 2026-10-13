using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.AspNetCore.Diagnostics;

namespace TeamFlow.Api.Http;

public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var failure = exception switch
        {
            ApiException api => api,
            DbUpdateConcurrencyException => new ApiException(409, "version_conflict", "Resource has changed. Reload before editing."),
            DbUpdateException { InnerException: PostgresException { SqlState: "23505" or "23503" } } => new ApiException(409, "data_conflict", "Operation conflicts with existing data."),
            BadHttpRequestException => new ApiException(400, "invalid_request", "Request format is invalid."),
            _ => null
        };
        context.Response.StatusCode = failure?.Status ?? StatusCodes.Status500InternalServerError;
        if (failure is null)
            logger.LogError(exception, "Request failed. TraceId: {TraceId}", context.TraceIdentifier);

        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new()
            {
                Status = context.Response.StatusCode,
                Title = failure?.Message ?? "An unexpected error occurred.",
                Extensions = { ["code"] = failure?.Code ?? "internal_error", ["traceId"] = context.TraceIdentifier }
            }
        });
    }
}
