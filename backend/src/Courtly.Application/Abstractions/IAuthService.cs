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

    /// <summary>Updates the caller's own personal data (name, email, city). The <paramref name="userId"/>
    /// comes from the JWT. Enforces email uniqueness and that the city exists; returns the updated projection.</summary>
    Task<UserInfoResponse> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default);

    /// <summary>Changes the caller's own password after confirming the current one (rubric §294).
    /// Throws a validation error keyed <c>currentPassword</c> when the current password is wrong.</summary>
    Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default);

    /// <summary>Replaces the caller's profile image after validating the content (MIME + magic bytes + size).
    /// Returns the updated projection (its <c>AvatarUrl</c> is now populated).</summary>
    Task<UserInfoResponse> UpdateAvatarAsync(Guid userId, byte[] bytes, string? contentType, CancellationToken ct = default);

    /// <summary>The caller's stored avatar bytes + content-type, or <c>null</c> when they have none.</summary>
    Task<(byte[] Bytes, string ContentType)?> GetAvatarAsync(Guid userId, CancellationToken ct = default);
}
