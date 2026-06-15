using Courtly.Application.Common.Errors;
using Courtly.Application.Common.Exceptions;

namespace Courtly.Api.Middleware;

/// <summary>
/// Single error boundary for the whole pipeline (rubric §3.4). Expected <see cref="AppException"/>s become
/// their mapped status with the authored message; anything else is logged in full and returned as a generic
/// 500 — the client never sees a stack trace or internal detail outside Development. Registered first so it
/// wraps everything downstream.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        var traceId = context.TraceIdentifier;
        var (status, body) = ErrorResponseFactory.Create(exception, _env.IsDevelopment(), traceId);

        if (exception is AppException)
        {
            // Expected business/validation/auth failure — log lightly with enough context to correlate.
            _logger.LogWarning(
                exception,
                "Request {Method} {Path} failed with {Status} (trace {TraceId}).",
                context.Request.Method, context.Request.Path, status, traceId);
        }
        else
        {
            // Unexpected — log the full exception (stack included) so the failure is reproducible server-side.
            _logger.LogError(
                exception,
                "Unhandled exception on {Method} {Path} (trace {TraceId}).",
                context.Request.Method, context.Request.Path, traceId);
        }

        if (context.Response.HasStarted)
        {
            _logger.LogWarning("Response already started for trace {TraceId}; cannot write error body.", traceId);
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(body, context.RequestAborted);
    }
}
