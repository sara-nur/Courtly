using Courtly.Contracts.Auth;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courtly.Tests.Auth;

/// <summary>
/// L1 atomicity: register / reset wrap their multiple SaveChanges in one explicit transaction, so a failure
/// partway leaves no half-written state. Runs on SQLite (relational) because the InMemory provider silently
/// ignores transactions; a <see cref="FailingSaveInterceptor"/> injects the mid-flow failure.
/// </summary>
public class AuthServiceAtomicityTests
{
    private const string Email = "atomic@example.com";
    private const string Password = "Passw0rd!";

    private static RegisterRequest NewRegister(string email = Email) =>
        new(email, Password, "Atom", "Ic", null);

    [Fact]
    public async Task Register_rolls_back_user_and_tokens_when_a_later_save_fails()
    {
        await using var h = await AuthSqliteHarness.CreateAsync();

        // Fail the 2nd save (role assignment) — after the user row is written but before tokens are issued.
        h.Interceptor.FailOnSave = 2;

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Auth.RegisterAsync(NewRegister()));

        // The transaction never committed, so nothing the flow wrote survives.
        Assert.Null(await h.UserManager.FindByEmailAsync(Email));
        Assert.False(await h.Db.RefreshTokens.AnyAsync());
    }

    [Fact]
    public async Task Reset_rolls_back_password_and_token_when_the_final_save_fails()
    {
        await using var h = await AuthSqliteHarness.CreateAsync();
        await h.Auth.RegisterAsync(NewRegister());
        await h.Auth.ForgotPasswordAsync(new ForgotPasswordRequest(Email));
        var rawToken = h.Email.LastToken!;

        // Fail the 2nd save of the reset flow — the one that stamps the token UsedAtUtc, after the new password
        // hash + security stamp were already saved. Both must roll back together.
        h.Interceptor.FailOnSave = 2;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.Auth.ResetPasswordAsync(new ResetPasswordRequest(Email, rawToken, "N3wPassw0rd!")));

        h.Interceptor.FailOnSave = null; // disarm so the assertions below can read/verify freely

        // The DB transaction rolled back, but a rollback does NOT revert the EF change tracker — it still holds the
        // mutated in-memory user/token (new password hash, stamped UsedAtUtc). Clear it so the reads below hit the
        // persisted (rolled-back) rows instead of the stale tracked instances via identity resolution.
        h.Db.ChangeTracker.Clear();

        var user = await h.UserManager.FindByEmailAsync(Email);
        Assert.NotNull(user);
        Assert.True(await h.UserManager.CheckPasswordAsync(user!, Password)); // old password still works
        Assert.Null((await h.Db.PasswordResetTokens.SingleAsync()).UsedAtUtc); // token still unused
    }
}
