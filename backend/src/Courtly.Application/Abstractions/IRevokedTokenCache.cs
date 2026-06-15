namespace Courtly.Application.Abstractions;

/// <summary>
/// Caches the access-token revocation denylist so the JWT validation event (which runs on <em>every</em>
/// authenticated request) does not hit the database each time (rubric §8.2: cache per-request reads in
/// <c>IMemoryCache</c> at the service level). Logout invalidates the cache so a freshly revoked <c>jti</c>
/// is rejected on the very next call.
/// </summary>
public interface IRevokedTokenCache
{
    /// <summary>True if the access token's <paramref name="jti"/> has been revoked (logged out).</summary>
    Task<bool> IsRevokedAsync(string jti, CancellationToken ct = default);

    /// <summary>Drops the cached denylist so the next check reloads it from the database (call after
    /// persisting a new revocation).</summary>
    void Invalidate();
}
