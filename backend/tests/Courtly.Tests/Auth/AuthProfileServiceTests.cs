using Courtly.Application.Common.Exceptions;
using Courtly.Contracts.Auth;
using Xunit;

namespace Courtly.Tests.Auth;

/// <summary>Feature 28 (backend slice): self-service profile edit, password change, and avatar
/// upload/serve. Password-change field-keying (§294) and email-uniqueness are the graded rules.</summary>
public class AuthProfileServiceTests
{
    private const string Email = "profile@example.com";
    private const string Password = "Passw0rd!";

    private static RegisterRequest NewRegister(string email = Email) =>
        new(email, Password, "First", "Last", null);

    // 8-byte PNG signature is enough for the content sniffer to classify the bytes as image/png.
    private static byte[] PngBytes() =>
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01, 0x02, 0x03 };

    private static async Task<Guid> RegisterUserAsync(AuthHarness h, string email = Email)
    {
        await h.Auth.RegisterAsync(NewRegister(email));
        var user = await h.UserManager.FindByEmailAsync(email);
        return user!.Id;
    }

    // --- Change password ------------------------------------------------------

    [Fact]
    public async Task ChangePassword_wrong_current_throws_Validation_keyed_currentPassword()
    {
        await using var h = await AuthHarness.CreateAsync();
        var userId = await RegisterUserAsync(h);

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => h.Auth.ChangePasswordAsync(userId, new ChangePasswordRequest("WrongPass1!", "NewPassw0rd!")));

        Assert.NotNull(ex.Errors);
        Assert.True(ex.Errors!.ContainsKey("currentPassword"));
    }

    [Fact]
    public async Task ChangePassword_weak_new_throws_Validation_keyed_newPassword()
    {
        await using var h = await AuthHarness.CreateAsync();
        var userId = await RegisterUserAsync(h);

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => h.Auth.ChangePasswordAsync(userId, new ChangePasswordRequest(Password, "weak")));

        Assert.NotNull(ex.Errors);
        Assert.True(ex.Errors!.ContainsKey("newPassword"));
    }

    [Fact]
    public async Task ChangePassword_succeeds_old_no_longer_valid_new_valid()
    {
        await using var h = await AuthHarness.CreateAsync();
        var userId = await RegisterUserAsync(h);
        const string newPassword = "NewPassw0rd!";

        await h.Auth.ChangePasswordAsync(userId, new ChangePasswordRequest(Password, newPassword));

        // New password authenticates; the old one no longer does.
        Assert.False(string.IsNullOrEmpty((await h.Auth.LoginAsync(new LoginRequest(Email, newPassword))).AccessToken));
        await Assert.ThrowsAsync<UnauthorizedException>(() => h.Auth.LoginAsync(new LoginRequest(Email, Password)));
    }

    // --- Update profile -------------------------------------------------------

    [Fact]
    public async Task UpdateProfile_updates_name_and_email()
    {
        await using var h = await AuthHarness.CreateAsync();
        var userId = await RegisterUserAsync(h);

        var result = await h.Auth.UpdateProfileAsync(
            userId, new UpdateProfileRequest("Ada", "Lovelace", "ada@example.com", null));

        Assert.Equal("Ada", result.FirstName);
        Assert.Equal("Lovelace", result.LastName);
        Assert.Equal("ada@example.com", result.Email);

        // The new email authenticates (login accepts username-or-email).
        Assert.False(string.IsNullOrEmpty(
            (await h.Auth.LoginAsync(new LoginRequest("ada@example.com", Password))).AccessToken));
    }

    [Fact]
    public async Task UpdateProfile_duplicate_email_throws_Validation_keyed_email()
    {
        await using var h = await AuthHarness.CreateAsync();
        await RegisterUserAsync(h, "taken@example.com");
        var moverId = await RegisterUserAsync(h, "mover@example.com");

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => h.Auth.UpdateProfileAsync(
                moverId, new UpdateProfileRequest("First", "Last", "taken@example.com", null)));

        Assert.NotNull(ex.Errors);
        Assert.True(ex.Errors!.ContainsKey("email"));
    }

    [Fact]
    public async Task UpdateProfile_unknown_city_throws_Validation_keyed_cityId()
    {
        await using var h = await AuthHarness.CreateAsync();
        var userId = await RegisterUserAsync(h);

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => h.Auth.UpdateProfileAsync(
                userId, new UpdateProfileRequest("First", "Last", Email, 999_999)));

        Assert.NotNull(ex.Errors);
        Assert.True(ex.Errors!.ContainsKey("cityId"));
    }

    // --- Avatar ---------------------------------------------------------------

    [Fact]
    public async Task UpdateAvatar_stores_bytes_and_populates_AvatarUrl_then_GetAvatar_returns_them()
    {
        await using var h = await AuthHarness.CreateAsync();
        var userId = await RegisterUserAsync(h);
        var bytes = PngBytes();

        var result = await h.Auth.UpdateAvatarAsync(userId, bytes, "image/png");
        Assert.Equal("/api/auth/me/avatar", result.AvatarUrl);

        var fetched = await h.Auth.GetAvatarAsync(userId);
        Assert.NotNull(fetched);
        Assert.Equal("image/png", fetched!.Value.ContentType);
        Assert.Equal(bytes, fetched.Value.Bytes);
    }

    [Fact]
    public async Task UpdateAvatar_rejects_content_type_mismatch()
    {
        await using var h = await AuthHarness.CreateAsync();
        var userId = await RegisterUserAsync(h);

        // PNG magic bytes declared as JPEG → the content guard rejects the spoof.
        await Assert.ThrowsAsync<ValidationException>(
            () => h.Auth.UpdateAvatarAsync(userId, PngBytes(), "image/jpeg"));
    }

    [Fact]
    public async Task GetAvatar_returns_null_when_user_has_no_avatar()
    {
        await using var h = await AuthHarness.CreateAsync();
        var userId = await RegisterUserAsync(h);

        Assert.Null(await h.Auth.GetAvatarAsync(userId));
    }
}
