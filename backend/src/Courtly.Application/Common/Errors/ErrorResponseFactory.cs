using Courtly.Application.Common.Exceptions;
using Courtly.Contracts.Errors;

namespace Courtly.Application.Common.Errors;

/// <summary>
/// Maps an exception to its HTTP status + standardized <see cref="ErrorResponse"/>. Pure and HTTP-free so
/// the mapping can be unit-tested without a request pipeline; <c>ExceptionHandlingMiddleware</c> calls this
/// and only does the I/O (write JSON, set status, log). Expected <see cref="AppException"/>s surface their
/// authored, user-safe message; anything else becomes a generic 500 that never leaks internals — the real
/// exception is logged on the server, not returned (rubric §3.4).
/// </summary>
public static class ErrorResponseFactory
{
    private const int InternalServerError = 500;
    private const string UnexpectedTitle = "An unexpected error occurred.";

    public static (int Status, ErrorResponse Body) Create(Exception exception, bool includeDetails, string? traceId)
    {
        if (exception is AppException app)
        {
            // Authored, safe message → Title. No stack trace for expected errors.
            return (app.StatusCode, new ErrorResponse
            {
                Status = app.StatusCode,
                Title = app.Message,
                TraceId = traceId,
                Errors = app.Errors,
            });
        }

        // Unexpected: generic title to the client; detail (stack) only in Development.
        return (InternalServerError, new ErrorResponse
        {
            Status = InternalServerError,
            Title = UnexpectedTitle,
            Detail = includeDetails ? exception.ToString() : null,
            TraceId = traceId,
        });
    }
}
