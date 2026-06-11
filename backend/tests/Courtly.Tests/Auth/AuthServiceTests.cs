using Courtly.Application.Auth;
using Courtly.Contracts.Auth;
using Courtly.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courtly.Tests.Auth;

/// <summary>Feature 5 DoD (auto): register role-stripping, login, refresh rotation + reuse, logout revocation.</summary>
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

        Assert.True(result.IsSuccess);
        Assert.False(string.IsNullOrEmpty(result.Value!.AccessToken));
        Assert.False(string.IsNullOrEmpty(result.Value.RefreshToken));

        var user = await h.UserManager.FindByEmailAsync(Email);
        var roles = await h.UserManager.GetRolesAsync(user!);
        Assert.Equal(new[] { Roles.User }, roles);
        Assert.DoesNotContain(Roles.Admin, roles);
        Assert.DoesNotContain(Roles.Staff, roles);
    }

    [Fact]
    public async Task Register_duplicate_email_returns_Conflict()
    {
        await using var h = await AuthHarness.CreateAsync();
        await h.Auth.RegisterAsync(NewRegister());

        var second = await h.Auth.RegisterAsync(NewRegister());

        Assert.Equal(AuthOutcome.Conflict, second.Outcome);
    }

    [Fact]
    public async Task Register_weak_password_returns_ValidationFailed()
    {
        await using var h = await AuthHarness.CreateAsync();

        var result = await h.Auth.RegisterAsync(new RegisterRequest("weak@example.com", "weak", "W", "K", null));

        Assert.Equal(AuthOutcome.ValidationFailed, result.Outcome);
    }

    [Fact]
    public async Task Login_correct_password_succeeds_wrong_password_fails()
    {
        await using var h = await AuthHarness.CreateAsync();
        await h.Auth.RegisterAsync(NewRegister());

        var ok = await h.Auth.LoginAsync(new LoginRequest(Email, Password));
        var bad = await h.Auth.LoginAsync(new LoginRequest(Email, "WrongPass1!"));

        Assert.True(ok.IsSuccess);
        Assert.False(string.IsNullOrEmpty(ok.Value!.AccessToken));
        Assert.Equal(AuthOutcome.InvalidCredentials, bad.Outcome);
    }

    [Fact]
    public async Task Refresh_rotates_token_and_rejects_reuse_of_old_token()
    {
        await using var h = await AuthHarness.CreateAsync();
        var r1 = (await h.Auth.RegisterAsync(NewRegister())).Value!.RefreshToken;

        var refreshed = await h.Auth.RefreshAsync(new RefreshRequest(r1));
        Assert.True(refreshed.IsSuccess);
        var r2 = refreshed.Value!.RefreshToken;
        Assert.NotEqual(r1, r2);

        var oldRow = await h.Db.RefreshTokens.SingleAsync(t => t.TokenHash == h.TokenService.HashToken(r1));
        Assert.NotNull(oldRow.RevokedAtUtc);
        Assert.Equal(h.TokenService.HashToken(r2), oldRow.ReplacedByTokenHash);

        Assert.Equal(AuthOutcome.InvalidToken, (await h.Auth.RefreshAsync(new RefreshRequest(r1))).Outcome);
        Assert.True((await h.Auth.RefreshAsync(new RefreshRequest(r2))).IsSuccess);
    }

    [Fact]
    public async Task Refresh_with_expired_token_returns_InvalidToken()
    {
        await using var h = await AuthHarness.CreateAsync();
        var token = (await h.Auth.RegisterAsync(NewRegister())).Value!.RefreshToken;

        h.Clock.UtcNow = h.Clock.UtcNow.AddDays(8); // past the 7-day refresh lifetime

        var result = await h.Auth.RefreshAsync(new RefreshRequest(token));
        Assert.Equal(AuthOutcome.InvalidToken, result.Outcome);
    }

    [Fact]
    public async Task Logout_revokes_access_jti_and_presented_refresh_token()
    {
        await using var h = await AuthHarness.CreateAsync();
        var refresh = (await h.Auth.RegisterAsync(NewRegister())).Value!.RefreshToken;
        var user = await h.UserManager.FindByEmailAsync(Email);
        var roles = await h.UserManager.GetRolesAsync(user!);
        var (_, jti, expiresAt) = h.TokenService.CreateAccessToken(user!, roles);

        var result = await h.Auth.LogoutAsync(user!.Id, jti, expiresAt, refresh);

        Assert.True(result.IsSuccess);
        Assert.True(await h.Db.RevokedTokens.AnyAsync(r => r.Jti == jti));
        var row = await h.Db.RefreshTokens.SingleAsync(t => t.TokenHash == h.TokenService.HashToken(refresh));
        Assert.NotNull(row.RevokedAtUtc);
    }
}
