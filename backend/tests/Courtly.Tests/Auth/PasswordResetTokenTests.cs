using Courtly.Application.Auth;
using Courtly.Contracts.Auth;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courtly.Tests.Auth;

/// <summary>Feature 5 DoD (auto): reset tokens are hashed, single-use, expiring, and non-enumerating.</summary>
public class PasswordResetTokenTests
{
    private const string Email = "reset-me@example.com";
    private const string OldPassword = "Passw0rd!";
    private const string NewPassword = "N3wPassw0rd!";

    private static async Task<AuthHarness> WithUserAsync()
    {
        var harness = await AuthHarness.CreateAsync();
        await harness.Auth.RegisterAsync(new RegisterRequest(Email, OldPassword, "Reset", "Me", null));
        return harness;
    }

    [Fact]
    public async Task Forgot_creates_hashed_token_row_and_emails_raw_token()
    {
        await using var h = await WithUserAsync();

        var result = await h.Auth.ForgotPasswordAsync(new ForgotPasswordRequest(Email));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, h.Email.SendCount);
        Assert.Equal(Email, h.Email.LastEmail);

        var raw = h.Email.LastToken!;
        var row = await h.Db.PasswordResetTokens.SingleAsync();
        Assert.Equal(h.TokenService.HashToken(raw), row.TokenHash);
        Assert.NotEqual(raw, row.TokenHash);
        Assert.Null(row.UsedAtUtc);
    }

    [Fact]
    public async Task Forgot_unknown_email_succeeds_without_row_or_email()
    {
        await using var h = await WithUserAsync();

        var result = await h.Auth.ForgotPasswordAsync(new ForgotPasswordRequest("nobody@example.com"));

        Assert.True(result.IsSuccess); // anti-enumeration: same outcome as a known email
        Assert.Equal(0, h.Email.SendCount);
        Assert.False(await h.Db.PasswordResetTokens.AnyAsync());
    }

    [Fact]
    public async Task Reset_with_valid_token_changes_password_and_marks_used()
    {
        await using var h = await WithUserAsync();
        await h.Auth.ForgotPasswordAsync(new ForgotPasswordRequest(Email));
        var raw = h.Email.LastToken!;

        var result = await h.Auth.ResetPasswordAsync(new ResetPasswordRequest(Email, raw, NewPassword));

        Assert.True(result.IsSuccess);
        var user = await h.UserManager.FindByEmailAsync(Email);
        Assert.True(await h.UserManager.CheckPasswordAsync(user!, NewPassword));
        Assert.False(await h.UserManager.CheckPasswordAsync(user!, OldPassword));
        Assert.NotNull((await h.Db.PasswordResetTokens.SingleAsync()).UsedAtUtc);
    }

    [Fact]
    public async Task Reset_reusing_used_token_fails()
    {
        await using var h = await WithUserAsync();
        await h.Auth.ForgotPasswordAsync(new ForgotPasswordRequest(Email));
        var raw = h.Email.LastToken!;
        await h.Auth.ResetPasswordAsync(new ResetPasswordRequest(Email, raw, NewPassword));

        var second = await h.Auth.ResetPasswordAsync(new ResetPasswordRequest(Email, raw, "An0therPass!"));

        Assert.Equal(AuthOutcome.InvalidToken, second.Outcome);
    }

    [Fact]
    public async Task Reset_with_expired_token_fails()
    {
        await using var h = await WithUserAsync();
        await h.Auth.ForgotPasswordAsync(new ForgotPasswordRequest(Email));
        var raw = h.Email.LastToken!;

        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(61); // past the 60-minute reset window

        var result = await h.Auth.ResetPasswordAsync(new ResetPasswordRequest(Email, raw, NewPassword));
        Assert.Equal(AuthOutcome.InvalidToken, result.Outcome);
    }
}
