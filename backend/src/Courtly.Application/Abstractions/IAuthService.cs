using Courtly.Contracts.Auth;

namespace Courtly.Application.Abstractions;

/// <summary>
/// All auth business logic. Controllers only model-bind, call these, and return the DTO. Failures are
/// signalled by throwing the app's custom exceptions (feature 6), which the exception middleware maps to a
/// standardized HTTP error. Returns DTOs only — never entities.
/// </summary>
public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default);

    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);

    Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken ct = default);

    /// <summary>Revokes the current access token's <paramref name="accessTokenJti"/> and the presented
    /// refresh token. Identifiers come from the caller's JWT, never the request body.</summary>
    Task LogoutAsync(
        Guid userId,
        string accessTokenJti,
        DateTime accessTokenExpiresAtUtc,
        string refreshToken,
        CancellationToken ct = default);

    Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default);

    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);

    Task<UserInfoResponse?> GetCurrentUserAsync(Guid userId, CancellationToken ct = default);
}
