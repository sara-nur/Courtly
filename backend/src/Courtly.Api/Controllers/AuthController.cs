using Courtly.Application.Abstractions;
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
}
