namespace Courtly.Application.Common.Exceptions;

/// <summary>
/// Base type for every <em>expected</em> application error. Carries the HTTP status the
/// <c>ExceptionHandlingMiddleware</c> should return and an optional field→messages map for validation
/// failures. Services throw these instead of returning result objects or throwing raw
/// <see cref="Exception"/> (rubric §3.4: custom exception types mapped to HTTP statuses by middleware).
/// Anything that is <b>not</b> an <see cref="AppException"/> is treated as an unexpected 500.
/// </summary>
public abstract class AppException : Exception
{
    protected AppException(int statusCode, string message, IReadOnlyDictionary<string, string[]>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    /// <summary>HTTP status code the client should receive.</summary>
    public int StatusCode { get; }

    /// <summary>Field-level validation messages, when applicable.</summary>
    public IReadOnlyDictionary<string, string[]>? Errors { get; }
}

/// <summary>400 — request failed server-side validation. Optionally carries per-field messages.</summary>
public sealed class ValidationException : AppException
{
    public ValidationException(string message, IReadOnlyDictionary<string, string[]>? errors = null)
        : base(StatusCodes.Status400BadRequest, message, errors)
    {
    }
}

/// <summary>401 — authentication failed (bad credentials, invalid/expired/revoked token).</summary>
public sealed class UnauthorizedException : AppException
{
    public UnauthorizedException(string message)
        : base(StatusCodes.Status401Unauthorized, message)
    {
    }
}

/// <summary>403 — authenticated but not allowed to perform the action / touch another user's data.</summary>
public sealed class ForbiddenException : AppException
{
    public ForbiddenException(string message)
        : base(StatusCodes.Status403Forbidden, message)
    {
    }
}

/// <summary>404 — the requested resource does not exist.</summary>
public sealed class NotFoundException : AppException
{
    public NotFoundException(string message)
        : base(StatusCodes.Status404NotFound, message)
    {
    }
}

/// <summary>409 — uniqueness conflict (e.g. duplicate email, slot just taken).</summary>
public sealed class ConflictException : AppException
{
    public ConflictException(string message)
        : base(StatusCodes.Status409Conflict, message)
    {
    }
}

/// <summary>409 — a business rule forbids the operation (e.g. delete-restrict on a referenced row,
/// an illegal reservation-status transition). The general-purpose rule-violation error.</summary>
public sealed class BusinessException : AppException
{
    public BusinessException(string message)
        : base(StatusCodes.Status409Conflict, message)
    {
    }
}

/// <summary>The handful of HTTP status codes the exception hierarchy maps to, kept here so the Application
/// layer needs no dependency on ASP.NET's <c>Microsoft.AspNetCore.Http.StatusCodes</c>.</summary>
internal static class StatusCodes
{
    public const int Status400BadRequest = 400;
    public const int Status401Unauthorized = 401;
    public const int Status403Forbidden = 403;
    public const int Status404NotFound = 404;
    public const int Status409Conflict = 409;
    public const int Status500InternalServerError = 500;
}
