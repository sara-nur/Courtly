using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Courtly.Application.Abstractions;
using Courtly.Domain.Entities;
using Courtly.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Courtly.Application.Auth;

/// <summary>
/// Issues JWT access tokens and opaque (refresh / password-reset) tokens, and owns the JWT validation
/// parameters. Opaque tokens are random bytes; only their SHA-256 hash is ever stored.
/// </summary>
public sealed class TokenService : ITokenService
{
    private const int OpaqueTokenBytes = 32; // 256 bits of entropy

    private readonly JwtOptions _options;
    private readonly IClock _clock;
    private readonly SymmetricSecurityKey _signingKey;

    public TokenService(IOptions<JwtOptions> options, IClock clock)
    {
        _options = options.Value;
        _clock = clock;
        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
    }

    public (string AccessToken, string Jti, DateTime ExpiresAtUtc) CreateAccessToken(AppUser user, IEnumerable<string> roles)
    {
        var jti = Guid.NewGuid().ToString("N");
        var issuedAt = _clock.UtcNow;
        var expiresAt = issuedAt.AddMinutes(_options.AccessMinutes);

        // Emit ClaimTypes.* (long URIs) and "jti" directly so the default inbound map round-trips them
        // unchanged — User.FindFirstValue(ClaimTypes.NameIdentifier) / IsInRole(...) work without remapping.
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, jti),
            new(ClaimTypes.Name, user.UserName ?? string.Empty),
        };
        if (!string.IsNullOrEmpty(user.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, user.Email));
        }
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: issuedAt,
            expires: expiresAt,
            signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256));

        var encoded = new JwtSecurityTokenHandler().WriteToken(token);
        return (encoded, jti, expiresAt);
    }

    public (string RawToken, string TokenHash, DateTime ExpiresAtUtc) CreateRefreshToken()
    {
        var raw = GenerateRawToken();
        return (raw, HashToken(raw), _clock.UtcNow.AddDays(_options.RefreshDays));
    }

    public (string RawToken, string TokenHash, DateTime ExpiresAtUtc) CreatePasswordResetToken()
    {
        var raw = GenerateRawToken();
        return (raw, HashToken(raw), _clock.UtcNow.AddMinutes(_options.ResetTokenMinutes));
    }

    public string HashToken(string rawToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hash);
    }

    public TokenValidationParameters BuildValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = _options.Issuer,
        ValidateAudience = true,
        ValidAudience = _options.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = _signingKey,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        NameClaimType = ClaimTypes.Name,
        RoleClaimType = ClaimTypes.Role,
    };

    private static string GenerateRawToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(OpaqueTokenBytes));
}
