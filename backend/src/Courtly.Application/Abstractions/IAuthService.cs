using Courtly.Application.Auth;
using Courtly.Contracts.Auth;

namespace Courtly.Application.Abstractions;

/// <summary>All auth business logic. Controllers only model-bind, call these, and map the
/// <see cref="AuthResult"/> outcome to a status code. Returns DTOs only — never entities.</summary>
public interface IAuthService
{
    Task<AuthResult<AuthResponse>> RegisterAsync(RegisterRequest request, CancellationToken ct = default);

    Task<AuthResult<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default);

    Task<AuthResult<AuthResponse>> RefreshAsync(RefreshRequest request, CancellationToken ct = default);

    /// <summary>Revokes the current access token's <paramref name="accessTokenJti"/> and the presented
    /// refresh token. Identifiers come from the caller's JWT, never the request body.</summary>
    Task<AuthResult> LogoutAsync(
        Guid userId,
        string accessTokenJti,
        DateTime accessTokenExpiresAtUtc,
        string refreshToken,
        CancellationToken ct = default);

    Task<AuthResult> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default);

    Task<AuthResult> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);

    Task<UserInfoResponse?> GetCurrentUserAsync(Guid userId, CancellationToken ct = default);
}
