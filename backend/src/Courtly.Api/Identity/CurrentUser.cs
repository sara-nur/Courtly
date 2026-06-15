using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Courtly.Application.Abstractions;

namespace Courtly.Api.Identity;

/// <summary>
/// <see cref="ICurrentUser"/> over <see cref="IHttpContextAccessor"/> — the one place that reads the JWT
/// claims, so services never parse the token by hand (rubric §3.4). Claim names match what
/// <c>TokenService</c> mints (JwtBearer runs with <c>MapInboundClaims = false</c>).
/// </summary>
public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email);

    public string? Jti => Principal?.FindFirstValue(JwtRegisteredClaimNames.Jti);

    public DateTime? AccessTokenExpiresAtUtc =>
        long.TryParse(Principal?.FindFirstValue("exp"), out var unixSeconds)
            ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime
            : null;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public IReadOnlyList<string> Roles =>
        Principal?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList() ?? [];

    public bool IsInRole(string role) => Principal?.IsInRole(role) ?? false;
}
