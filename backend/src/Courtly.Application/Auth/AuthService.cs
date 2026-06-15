using Courtly.Application.Abstractions;
using Courtly.Application.Common.Exceptions;
using Courtly.Contracts.Auth;
using Courtly.Domain.Constants;
using Courtly.Domain.Entities;
using Courtly.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Courtly.Application.Auth;

/// <summary>All auth business logic (register/login/refresh/logout/forgot/reset). Returns DTOs only and
/// signals failures by throwing the app's custom exceptions (feature 6).</summary>
public sealed class AuthService : IAuthService
{
    private readonly UserManager<AppUser> _userManager;
    private readonly CourtlyDbContext _db;
    private readonly ITokenService _tokenService;
    private readonly IEmailSender _emailSender;
    private readonly IClock _clock;
    private readonly IRevokedTokenCache _revokedTokenCache;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        UserManager<AppUser> userManager,
        CourtlyDbContext db,
        ITokenService tokenService,
        IEmailSender emailSender,
        IClock clock,
        IRevokedTokenCache revokedTokenCache,
        ILogger<AuthService> logger)
    {
        _userManager = userManager;
        _db = db;
        _tokenService = tokenService;
        _emailSender = emailSender;
        _clock = clock;
        _revokedTokenCache = revokedTokenCache;
        _logger = logger;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        if (await _userManager.FindByEmailAsync(request.Email) is not null)
        {
            throw new ConflictException("Email is already registered.");
        }

        var user = new AppUser
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            CityId = request.CityId,
            IsActive = true,
            CreatedAtUtc = _clock.UtcNow,
        };

        var created = await _userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            throw new ValidationException(DescribeErrors(created), ToFieldErrors(created, nameof(request.Password)));
        }

        // Register hardening: role is never taken from the request — every self-registration is a User.
        await _userManager.AddToRoleAsync(user, Roles.User);
        _logger.LogInformation("Registered new user {UserId} ({Email}).", user.Id, user.Email);

        return await IssueTokensAsync(user, ct);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await FindByNameOrEmailAsync(request.UserNameOrEmail);
        if (user is null || !user.IsActive || !await _userManager.CheckPasswordAsync(user, request.Password))
        {
            // Single message regardless of which check failed — no account enumeration.
            throw new UnauthorizedException("Invalid credentials.");
        }

        return await IssueTokensAsync(user, ct);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var hash = _tokenService.HashToken(request.RefreshToken);
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        // Reject unknown, already-rotated (reuse), or expired tokens.
        if (stored is null || stored.RevokedAtUtc is not null || stored.ExpiresAtUtc <= now)
        {
            throw new UnauthorizedException("Invalid or expired refresh token.");
        }

        var user = await _userManager.FindByIdAsync(stored.UserId.ToString());
        if (user is null || !user.IsActive)
        {
            throw new UnauthorizedException("Invalid refresh token.");
        }

        var roles = await _userManager.GetRolesAsync(user);
        var (accessToken, _, accessExpires) = _tokenService.CreateAccessToken(user, roles);
        var (rawRefresh, refreshHash, refreshExpires) = _tokenService.CreateRefreshToken();

        // Rotate: revoke the presented token and issue a successor in the same SaveChanges.
        stored.RevokedAtUtc = now;
        stored.ReplacedByTokenHash = refreshHash;
        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshHash,
            ExpiresAtUtc = refreshExpires,
            CreatedAtUtc = now,
        });
        await _db.SaveChangesAsync(ct);

        return new AuthResponse(accessToken, rawRefresh, accessExpires, ToUserInfo(user, roles));
    }

    public async Task LogoutAsync(
        Guid userId,
        string accessTokenJti,
        DateTime accessTokenExpiresAtUtc,
        string refreshToken,
        CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        // Denylist the access token's jti (idempotent — the unique index also guards duplicates).
        if (!await _db.RevokedTokens.AnyAsync(r => r.Jti == accessTokenJti, ct))
        {
            _db.RevokedTokens.Add(new RevokedToken
            {
                UserId = userId,
                Jti = accessTokenJti,
                ExpiresAtUtc = accessTokenExpiresAtUtc,
                RevokedAtUtc = now,
            });
        }

        // Revoke the presented refresh token only if it belongs to the caller — otherwise ignore silently.
        var hash = _tokenService.HashToken(refreshToken);
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is not null && stored.UserId == userId && stored.RevokedAtUtc is null)
        {
            stored.RevokedAtUtc = now;
        }

        await _db.SaveChangesAsync(ct);

        // Drop the cached denylist so the just-revoked jti is rejected on the very next request.
        _revokedTokenCache.Invalidate();
        _logger.LogInformation("User {UserId} logged out (jti {Jti} revoked).", userId, accessTokenJti);
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive)
        {
            // Anti-enumeration: always succeed, without creating a token or sending mail.
            return;
        }

        var (rawToken, tokenHash, expiresAt) = _tokenService.CreatePasswordResetToken();
        _db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAtUtc = expiresAt,
            CreatedAtUtc = _clock.UtcNow,
        });
        await _db.SaveChangesAsync(ct);

        await _emailSender.SendPasswordResetAsync(user.Email!, rawToken, ct);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            throw new UnauthorizedException("Invalid or expired reset token.");
        }

        var now = _clock.UtcNow;
        var hash = _tokenService.HashToken(request.Token);
        var stored = await _db.PasswordResetTokens
            .FirstOrDefaultAsync(t => t.UserId == user.Id && t.TokenHash == hash, ct);

        if (stored is null || stored.UsedAtUtc is not null || stored.ExpiresAtUtc <= now)
        {
            throw new UnauthorizedException("Invalid or expired reset token.");
        }

        // Validate the new password against Identity's configured policy first, so a policy-rejected attempt
        // leaves the reset token unused (retryable). Then set the hash directly and rotate the security stamp
        // (invalidating other sessions) — avoids Identity's data-protection token providers entirely.
        foreach (var validator in _userManager.PasswordValidators)
        {
            var validation = await validator.ValidateAsync(_userManager, user, request.NewPassword);
            if (!validation.Succeeded)
            {
                throw new ValidationException(
                    DescribeErrors(validation), ToFieldErrors(validation, nameof(request.NewPassword)));
            }
        }

        user.PasswordHash = _userManager.PasswordHasher.HashPassword(user, request.NewPassword);
        stored.UsedAtUtc = now;
        await _userManager.UpdateSecurityStampAsync(user);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Password reset completed for user {UserId}.", user.Id);
    }

    public async Task<UserInfoResponse?> GetCurrentUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return null;
        }

        var roles = await _userManager.GetRolesAsync(user);
        return ToUserInfo(user, roles);
    }

    private async Task<AuthResponse> IssueTokensAsync(AppUser user, CancellationToken ct)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var (accessToken, _, accessExpires) = _tokenService.CreateAccessToken(user, roles);
        var (rawRefresh, refreshHash, refreshExpires) = _tokenService.CreateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshHash,
            ExpiresAtUtc = refreshExpires,
            CreatedAtUtc = _clock.UtcNow,
        });
        await _db.SaveChangesAsync(ct);

        return new AuthResponse(accessToken, rawRefresh, accessExpires, ToUserInfo(user, roles));
    }

    private async Task<AppUser?> FindByNameOrEmailAsync(string userNameOrEmail) =>
        await _userManager.FindByNameAsync(userNameOrEmail)
        ?? await _userManager.FindByEmailAsync(userNameOrEmail);

    private static UserInfoResponse ToUserInfo(AppUser user, IList<string> roles) =>
        new(user.Id,
            user.UserName ?? string.Empty,
            user.Email ?? string.Empty,
            user.FirstName,
            user.LastName,
            user.CityId,
            roles.ToList());

    private static string DescribeErrors(IdentityResult result) =>
        string.Join(" ", result.Errors.Select(e => e.Description));

    private static IReadOnlyDictionary<string, string[]> ToFieldErrors(IdentityResult result, string field) =>
        new Dictionary<string, string[]>
        {
            [field] = result.Errors.Select(e => e.Description).ToArray(),
        };
}
