using Courtly.Application.Common.Exceptions;
using Courtly.Contracts.Auth;
using Courtly.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courtly.Tests.Auth;

/// <summary>Feature 5 DoD (auto): register role-stripping, login, refresh rotation + reuse, logout
/// revocation. Updated for feature 6 — failures now surface as the app's custom exceptions.</summary>
public class AuthServiceTests
{
    private const string Email = "newuser@example.com";
    private const string Password = "Passw0rd!";

    private static RegisterRequest NewRegister(string email = Email) =>
        new(email, Password, "New", "User", null);

    [Fact]
    public async Task Register_grants_only_User_role_and_returns_tokens()
    {
        await using var h = await AuthHarness.CreateAsync();

        var result = await h.Auth.RegisterAsync(NewRegister());

        Assert.False(string.IsNullOrEmpty(result.AccessToken));
        Assert.False(string.IsNullOrEmpty(result.RefreshToken));

        var user = await h.UserManager.FindByEmailAsync(Email);
        var roles = await h.UserManager.GetRolesAsync(user!);
        Assert.Equal(new[] { Roles.User }, roles);
        Assert.DoesNotContain(Roles.Admin, roles);
        Assert.DoesNotContain(Roles.Staff, roles);
    }

    [Fact]
    public async Task Register_duplicate_email_throws_Conflict()
    {
        await using var h = await AuthHarness.CreateAsync();
        await h.Auth.RegisterAsync(NewRegister());

        await Assert.ThrowsAsync<ConflictException>(() => h.Auth.RegisterAsync(NewRegister()));
    }

    [Fact]
    public async Task Register_weak_password_throws_Validation()
    {
        await using var h = await AuthHarness.CreateAsync();

        await Assert.ThrowsAsync<ValidationException>(
            () => h.Auth.RegisterAsync(new RegisterRequest("weak@example.com", "weak", "W", "K", null)));
    }

    [Fact]
    public async Task Login_correct_password_succeeds_wrong_password_throws()
    {
        await using var h = await AuthHarness.CreateAsync();
        await h.Auth.RegisterAsync(NewRegister());

        var ok = await h.Auth.LoginAsync(new LoginRequest(Email, Password));
        Assert.False(string.IsNullOrEmpty(ok.AccessToken));

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => h.Auth.LoginAsync(new LoginRequest(Email, "WrongPass1!")));
    }

    [Fact]
    public async Task Refresh_rotates_token_and_rejects_reuse_of_old_token()
    {
        await using var h = await AuthHarness.CreateAsync();
        var r1 = (await h.Auth.RegisterAsync(NewRegister())).RefreshToken;

        var refreshed = await h.Auth.RefreshAsync(new RefreshRequest(r1));
        var r2 = refreshed.RefreshToken;
        Assert.NotEqual(r1, r2);

        var oldRow = await h.Db.RefreshTokens.SingleAsync(t => t.TokenHash == h.TokenService.HashToken(r1));
        Assert.NotNull(oldRow.RevokedAtUtc);
        Assert.Equal(h.TokenService.HashToken(r2), oldRow.ReplacedByTokenHash);

        await Assert.ThrowsAsync<UnauthorizedException>(() => h.Auth.RefreshAsync(new RefreshRequest(r1)));
        Assert.False(string.IsNullOrEmpty((await h.Auth.RefreshAsync(new RefreshRequest(r2))).AccessToken));
    }

    [Fact]
    public async Task Refresh_with_expired_token_throws_Unauthorized()
    {
        await using var h = await AuthHarness.CreateAsync();
        var token = (await h.Auth.RegisterAsync(NewRegister())).RefreshToken;

        h.Clock.UtcNow = h.Clock.UtcNow.AddDays(8); // past the 7-day refresh lifetime

        await Assert.ThrowsAsync<UnauthorizedException>(() => h.Auth.RefreshAsync(new RefreshRequest(token)));
    }

    [Fact]
    public async Task Logout_revokes_access_jti_and_presented_refresh_token()
    {
        await using var h = await AuthHarness.CreateAsync();
        var refresh = (await h.Auth.RegisterAsync(NewRegister())).RefreshToken;
        var user = await h.UserManager.FindByEmailAsync(Email);
        var roles = await h.UserManager.GetRolesAsync(user!);
        var (_, jti, expiresAt) = h.TokenService.CreateAccessToken(user!, roles);

        await h.Auth.LogoutAsync(user!.Id, jti, expiresAt, refresh);

        Assert.True(await h.Db.RevokedTokens.AnyAsync(r => r.Jti == jti));
        var row = await h.Db.RefreshTokens.SingleAsync(t => t.TokenHash == h.TokenService.HashToken(refresh));
        Assert.NotNull(row.RevokedAtUtc);
    }
}
