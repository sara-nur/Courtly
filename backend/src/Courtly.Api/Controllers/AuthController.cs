using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Courtly.Application.Abstractions;
using Courtly.Application.Auth;
using Courtly.Contracts.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Courtly.Api.Controllers;

/// <summary>
/// Auth & identity endpoints (feature 5). Anonymous: register/login/refresh/forgot/reset.
/// Authenticated: logout and the <c>/me</c> stub. Caller identity always comes from the JWT.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth) => _auth = auth;

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
        => Map(await _auth.RegisterAsync(request, ct));

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
        => Map(await _auth.LoginAsync(request, ct));

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken ct)
        => Map(await _auth.RefreshAsync(request, ct));

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken ct)
    {
        // Identity comes from the validated token, never the body (ownership-from-JWT rule).
        var userId = GetUserId();
        var jti = User.FindFirstValue(JwtRegisteredClaimNames.Jti);
        var expiresAt = GetAccessTokenExpiry();
        if (userId is null || string.IsNullOrEmpty(jti) || expiresAt is null)
        {
            return Unauthorized();
        }

        var result = await _auth.LogoutAsync(userId.Value, jti, expiresAt.Value, request.RefreshToken, ct);
        return result.IsSuccess ? NoContent() : MapFailure(result);
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
        var result = await _auth.ResetPasswordAsync(request, ct);
        return result.IsSuccess ? Ok() : MapFailure(result);
    }

    /// <summary>Protected stub proving <c>[Authorize]</c> + the jti denylist work end-to-end.</summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var user = await _auth.GetCurrentUserAsync(userId.Value, ct);
        return user is null ? Unauthorized() : Ok(user);
    }

    private Guid? GetUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private DateTime? GetAccessTokenExpiry() =>
        long.TryParse(User.FindFirstValue("exp"), out var unixSeconds)
            ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime
            : null;

    private IActionResult Map(AuthResult<AuthResponse> result) =>
        result.IsSuccess ? Ok(result.Value) : MapFailure(result);

    // F6 replaces this ad-hoc mapping with ExceptionHandlingMiddleware + a standardized ErrorResponse.
    private IActionResult MapFailure(AuthResult result) => result.Outcome switch
    {
        AuthOutcome.ValidationFailed => BadRequest(new { error = result.Error }),
        AuthOutcome.InvalidCredentials => Unauthorized(new { error = result.Error }),
        AuthOutcome.InvalidToken => Unauthorized(new { error = result.Error }),
        AuthOutcome.Conflict => Conflict(new { error = result.Error }),
        AuthOutcome.NotFound => NotFound(new { error = result.Error }),
        _ => BadRequest(new { error = result.Error }),
    };
}
