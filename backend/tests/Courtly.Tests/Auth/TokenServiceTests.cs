using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Courtly.Application.Auth;
using Courtly.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Courtly.Tests.Auth;

/// <summary>Feature 5 DoD (auto): JWT issue/validate, signature integrity, expiry, and refresh-token hashing.</summary>
public class TokenServiceTests
{
    private static (TokenService Service, TestClock Clock) NewService()
    {
        var clock = new TestClock();
        return (new TokenService(Options.Create(AuthHarness.DefaultJwt()), clock), clock);
    }

    private static AppUser SampleUser() => new()
    {
        Id = Guid.NewGuid(),
        UserName = "alice@example.com",
        Email = "alice@example.com",
        FirstName = "Alice",
        LastName = "Doe",
    };

    private static ClaimsPrincipal Validate(TokenService service, string token)
    {
        // MapInboundClaims=false matches the JwtBearer config so claim names round-trip exactly.
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        return handler.ValidateToken(token, service.BuildValidationParameters(), out _);
    }

    [Fact]
    public void Access_token_validates_with_expected_subject_jti_roles_and_expiry()
    {
        var (service, clock) = NewService();
        // Lifetime is validated against the real wall clock, so issue at "now" to keep the token valid here.
        clock.UtcNow = DateTime.UtcNow;
        var user = SampleUser();

        var (token, jti, expiresAt) = service.CreateAccessToken(user, new[] { "User", "Admin" });
        var principal = Validate(service, token);

        Assert.Equal(user.Id.ToString(), principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal(jti, principal.FindFirstValue(JwtRegisteredClaimNames.Jti));
        Assert.True(principal.IsInRole("User"));
        Assert.True(principal.IsInRole("Admin"));
        Assert.Equal(clock.UtcNow.AddMinutes(15), expiresAt); // AccessMinutes from DefaultJwt
    }

    [Fact]
    public void Tampered_signature_fails_validation()
    {
        var (service, _) = NewService();
        var (token, _, _) = service.CreateAccessToken(SampleUser(), new[] { "User" });

        var parts = token.Split('.');
        parts[2] = (parts[2][0] == 'A' ? 'B' : 'A') + parts[2][1..]; // corrupt the signature segment
        var tampered = string.Join('.', parts);

        Assert.ThrowsAny<SecurityTokenException>(() => Validate(service, tampered));
    }

    [Fact]
    public void Expired_token_is_rejected()
    {
        var (service, clock) = NewService();
        // Issue "in the past" so the 15-minute lifetime is already over relative to real validation time.
        clock.UtcNow = DateTime.UtcNow.AddHours(-1);
        var (token, _, _) = service.CreateAccessToken(SampleUser(), new[] { "User" });

        Assert.Throws<SecurityTokenExpiredException>(() => Validate(service, token));
    }

    [Fact]
    public void HashToken_is_deterministic_and_differs_from_raw()
    {
        var (service, _) = NewService();
        var (raw, hash, _) = service.CreateRefreshToken();

        Assert.Equal(hash, service.HashToken(raw));
        Assert.NotEqual(raw, hash);
    }
}
