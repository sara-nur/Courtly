using Courtly.Domain.Entities;
using Microsoft.IdentityModel.Tokens;

namespace Courtly.Application.Abstractions;

/// <summary>Mints and describes the tokens used by auth: the JWT access token, opaque refresh tokens,
/// and password-reset tokens. Opaque tokens are returned raw to the caller but only their SHA-256 hash
/// is persisted.</summary>
public interface ITokenService
{
    /// <summary>Creates a signed JWT. Returns the token, its <c>jti</c> (so logout can revoke it), and
    /// its absolute UTC expiry.</summary>
    (string AccessToken, string Jti, DateTime ExpiresAtUtc) CreateAccessToken(AppUser user, IEnumerable<string> roles);

    /// <summary>Creates an opaque refresh token: the raw value (returned to the client), its hash (stored),
    /// and its expiry.</summary>
    (string RawToken, string TokenHash, DateTime ExpiresAtUtc) CreateRefreshToken();

    /// <summary>Creates an opaque single-use password-reset token: raw value (emailed), hash (stored), expiry.</summary>
    (string RawToken, string TokenHash, DateTime ExpiresAtUtc) CreatePasswordResetToken();

    /// <summary>SHA-256 hash of a presented raw token, used to look up the stored row.</summary>
    string HashToken(string rawToken);

    /// <summary>Single source of truth for JWT validation (issuer/audience/lifetime/signing key),
    /// reused by JwtBearer at startup and by tests.</summary>
    TokenValidationParameters BuildValidationParameters();
}
