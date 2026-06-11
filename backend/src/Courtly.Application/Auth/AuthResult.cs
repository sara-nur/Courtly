namespace Courtly.Application.Auth;

/// <summary>Coarse auth outcome the controller maps to HTTP status codes.
/// Feature 6 replaces this with exception middleware + a standardized <c>ErrorResponse</c>.</summary>
public enum AuthOutcome
{
    Success,
    ValidationFailed,
    InvalidCredentials,
    Conflict,
    InvalidToken,
    NotFound,
}

/// <summary>Result of an auth operation with no payload.</summary>
public class AuthResult
{
    public AuthOutcome Outcome { get; }
    public string? Error { get; }
    public bool IsSuccess => Outcome == AuthOutcome.Success;

    protected AuthResult(AuthOutcome outcome, string? error)
    {
        Outcome = outcome;
        Error = error;
    }

    public static AuthResult Success() => new(AuthOutcome.Success, null);
    public static AuthResult Fail(AuthOutcome outcome, string? error = null) => new(outcome, error);
}

/// <summary>Result of an auth operation carrying a value on success.</summary>
public sealed class AuthResult<T> : AuthResult
{
    public T? Value { get; }

    private AuthResult(AuthOutcome outcome, T? value, string? error) : base(outcome, error) => Value = value;

    public static AuthResult<T> Ok(T value) => new(AuthOutcome.Success, value, null);
    public static new AuthResult<T> Fail(AuthOutcome outcome, string? error = null) => new(outcome, default, error);
}
