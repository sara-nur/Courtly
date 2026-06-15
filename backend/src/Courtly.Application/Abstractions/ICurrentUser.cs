namespace Courtly.Application.Abstractions;

/// <summary>
/// Request-scoped view of the authenticated caller, read from the validated JWT. Services depend on this
/// instead of parsing claims by hand (rubric §3.4: inject <c>IHttpContextAccessor</c> rather than manually
/// parsing the token; ownership always comes from the token, never the route/body). The Api layer supplies
/// the implementation over <c>IHttpContextAccessor</c>.
/// </summary>
public interface ICurrentUser
{
    /// <summary>The caller's id from the <c>nameidentifier</c> claim, or null when unauthenticated.</summary>
    Guid? UserId { get; }

    /// <summary>The caller's email claim, or null when unauthenticated.</summary>
    string? Email { get; }

    /// <summary>The access token's <c>jti</c>, used to denylist it on logout.</summary>
    string? Jti { get; }

    /// <summary>The access token's expiry (from the <c>exp</c> claim), used when revoking on logout.</summary>
    DateTime? AccessTokenExpiresAtUtc { get; }

    bool IsAuthenticated { get; }

    IReadOnlyList<string> Roles { get; }

    bool IsInRole(string role);
}
