using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace BettingAnalysis.Middleware;

public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Unhandled exception on {Method} {Path}: {Message}",
            httpContext.Request.Method,
            httpContext.Request.Path,
            exception.Message);

        var (status, title, detail) = exception switch
        {
            ArgumentException e         => (400, "Invalid Argument",     e.Message),
            InvalidOperationException e => (400, "Invalid Operation",    e.Message),
            KeyNotFoundException e      => (404, "Not Found",            e.Message),
            UnauthorizedAccessException => (401, "Unauthorized",         (string?)null),
            _                          => (500, "Internal Server Error", "An unexpected error occurred.")
        };

        httpContext.Response.StatusCode = status;

        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title  = title,
            Detail = detail,
        }, cancellationToken);

        return true;
    }
}
