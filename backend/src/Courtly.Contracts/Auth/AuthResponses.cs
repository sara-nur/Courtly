namespace Courtly.Contracts.Auth;

/// <summary>Returned by login, register (auto-login), and refresh. The refresh token is the raw value;
/// only its hash is persisted server-side.</summary>
public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAtUtc,
    UserInfoResponse User);

/// <summary>Public projection of an authenticated user (also the <c>GET /api/auth/me</c> body).
/// <paramref name="AvatarUrl"/> is a relative path (<c>/api/auth/me/avatar</c>) when the caller has a
/// profile image, or <c>null</c> — the client renders a placeholder when it is null and never receives
/// image bytes on this path (rubric: list/detail payloads carry only a URL, never base64).</summary>
public sealed record UserInfoResponse(
    Guid Id,
    string UserName,
    string Email,
    string FirstName,
    string LastName,
    long? CityId,
    IReadOnlyList<string> Roles,
    string? AvatarUrl = null);
