using Courtly.Application.Abstractions;
using Courtly.Application.Common.Exceptions;
using Courtly.Application.Courts.Media;
using Courtly.Contracts.Auth;
using Courtly.Contracts.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Courtly.Api.Controllers;

/// <summary>
/// Auth &amp; identity endpoints (feature 5). Anonymous: register/login/refresh/forgot/reset.
/// Authenticated: logout and the <c>/me</c> stub. Caller identity always comes from the JWT
/// (via <see cref="ICurrentUser"/>, never the body); the service throws on failure and the exception
/// middleware returns a standardized <see cref="ErrorResponse"/>.
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly ICurrentUser _currentUser;

    public AuthController(IAuthService auth, ICurrentUser currentUser)
    {
        _auth = auth;
        _currentUser = currentUser;
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct)
        => Ok(await _auth.RegisterAsync(request, ct));

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
        => Ok(await _auth.LoginAsync(request, ct));

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshRequest request, CancellationToken ct)
        => Ok(await _auth.RefreshAsync(request, ct));

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken ct)
    {
        // Identity comes from the validated token, never the body (ownership-from-JWT rule).
        var userId = _currentUser.UserId;
        var jti = _currentUser.Jti;
        var expiresAt = _currentUser.AccessTokenExpiresAtUtc;
        if (userId is null || string.IsNullOrEmpty(jti) || expiresAt is null)
        {
            return Unauthorized();
        }

        await _auth.LogoutAsync(userId.Value, jti, expiresAt.Value, request.RefreshToken, ct);
        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        // Always 200 regardless of whether the email exists — no account enumeration.
        await _auth.ForgotPasswordAsync(request, ct);
        return Ok();
    }

    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        await _auth.ResetPasswordAsync(request, ct);
        return Ok();
    }

    /// <summary>Protected stub proving <c>[Authorize]</c> + the jti denylist work end-to-end.</summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserInfoResponse>> Me(CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        var user = await _auth.GetCurrentUserAsync(userId.Value, ct);
        return user is null ? Unauthorized() : Ok(user);
    }

    /// <summary>Updates the caller's own profile (name, email, city). Identity is taken from the JWT, never
    /// the body. Returns the updated projection so the client refreshes its cached user in one round-trip.</summary>
    [Authorize]
    [HttpPut("me")]
    public async Task<ActionResult<UserInfoResponse>> UpdateProfile(UpdateProfileRequest request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        return Ok(await _auth.UpdateProfileAsync(userId.Value, request, ct));
    }

    /// <summary>Changes the caller's own password, confirming the current one first (rubric §294).</summary>
    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        await _auth.ChangePasswordAsync(userId.Value, request, ct);
        return NoContent();
    }

    /// <summary>Replaces the caller's profile image (multipart/form-data). Mirrors the court-image upload:
    /// reject empty/oversize up front from the declared length, then hand the bytes to the service which
    /// validates the content (MIME + magic bytes + size).</summary>
    [Authorize]
    [HttpPut("me/avatar")]
    [RequestSizeLimit(5_242_880)]
    [RequestFormLimits(MultipartBodyLengthLimit = 5_242_880)]
    public async Task<ActionResult<UserInfoResponse>> UpdateAvatar(IFormFile file, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        if (file is null || file.Length == 0)
        {
            throw new ValidationException(
                "An image file is required.",
                new Dictionary<string, string[]> { ["file"] = new[] { "An image file is required." } });
        }

        if (file.Length > ImageContentValidator.MaxBytes)
        {
            throw new ValidationException(
                "Image too large.",
                new Dictionary<string, string[]>
                {
                    ["file"] = new[] { $"Image must be at most {ImageContentValidator.MaxBytes / (1024 * 1024)} MB." },
                });
        }

        // Length is bounded, so size the buffer once (avoids MemoryStream's grow-and-copy churn).
        using var ms = new MemoryStream((int)file.Length);
        await file.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();

        return Ok(await _auth.UpdateAvatarAsync(userId.Value, bytes, file.ContentType, ct));
    }

    /// <summary>Streams the caller's own avatar bytes with the stored content-type; 404 when they have none.
    /// Self-only (identity from the JWT) — user images are access-controlled, unlike public court images.</summary>
    [Authorize]
    [HttpGet("me/avatar")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAvatar(CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        var content = await _auth.GetAvatarAsync(userId.Value, ct);
        if (content is null)
        {
            return NotFound();
        }

        // The bytes are user-uploaded — stop browsers MIME-sniffing away from the stored (validated) type.
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        // Unlike immutable court images, an avatar changes over time — force revalidation on each load.
        Response.Headers.CacheControl = "private, no-cache";
        return File(content.Value.Bytes, content.Value.ContentType);
    }
}
