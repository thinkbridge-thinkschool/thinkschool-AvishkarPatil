using Microsoft.AspNetCore.Mvc;
using QuotesApi.Exceptions;

namespace QuotesApi.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(
        RequestDelegate next,
        ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (DomainException ex)
        {
            _logger.LogWarning(ex, "Domain invariant violation");

            context.Response.StatusCode = 400;

            await context.Response.WriteAsJsonAsync(
                new ProblemDetails
                {
                    Title = "Bad Request",
                    Detail = ex.Message,
                    Status = 400
                });
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Request was cancelled");
            context.Response.StatusCode = 499; // Client Closed Request
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");

            context.Response.StatusCode = 500;

            await context.Response.WriteAsJsonAsync(
                new ProblemDetails
                {
                    Title = "Server Error",
                    Detail = ex.Message,
                    Status = 500
                });
        }
    }
}