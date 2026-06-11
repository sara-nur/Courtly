using System.ComponentModel.DataAnnotations;

namespace Courtly.Contracts.Auth;

/// <summary>
/// Self-service registration. Deliberately has <b>no</b> role/IsActive/Id field — a client cannot
/// self-assign a role because the binder has nothing to bind. The service always grants <c>User</c>.
/// </summary>
public sealed record RegisterRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password,
    [Required] string FirstName,
    [Required] string LastName,
    long? CityId);

public sealed record LoginRequest(
    [Required] string UserNameOrEmail,
    [Required] string Password);

public sealed record RefreshRequest(
    [Required] string RefreshToken);

public sealed record LogoutRequest(
    [Required] string RefreshToken);

public sealed record ForgotPasswordRequest(
    [Required, EmailAddress] string Email);

public sealed record ResetPasswordRequest(
    [Required, EmailAddress] string Email,
    [Required] string Token,
    [Required] string NewPassword);
