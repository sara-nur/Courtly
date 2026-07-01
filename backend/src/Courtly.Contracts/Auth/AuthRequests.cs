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

/// <summary>Edits the caller's own profile (feature 28). Identity comes from the JWT, never the body —
/// there is no Id field. <c>Email</c> doubles as a login identifier; the service enforces uniqueness.</summary>
public sealed record UpdateProfileRequest(
    [Required] string FirstName,
    [Required] string LastName,
    [Required, EmailAddress] string Email,
    long? CityId);

/// <summary>Changes the caller's own password (feature 28, rubric §294 — the user must confirm the
/// current password). The new-password confirmation is validated client-side; the server only needs the
/// current and new values.</summary>
public sealed record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required] string NewPassword);
