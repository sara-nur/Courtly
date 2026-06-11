namespace Courtly.Contracts.Auth;

/// <summary>Returned by login, register (auto-login), and refresh. The refresh token is the raw value;
/// only its hash is persisted server-side.</summary>
public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAtUtc,
    UserInfoResponse User);

/// <summary>Public projection of an authenticated user (also the <c>GET /api/auth/me</c> body).</summary>
public sealed record UserInfoResponse(
    Guid Id,
    string UserName,
    string Email,
    string FirstName,
    string LastName,
    long? CityId,
    IReadOnlyList<string> Roles);
