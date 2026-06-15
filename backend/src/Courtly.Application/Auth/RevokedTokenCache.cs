using Courtly.Application.Abstractions;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Courtly.Application.Auth;

/// <summary>
/// <see cref="IRevokedTokenCache"/> backed by <see cref="IMemoryCache"/>. Holds the set of still-valid
/// revoked <c>jti</c>s under one key with a short TTL, so the per-request denylist check is an in-memory
/// lookup instead of a database round-trip. Scoped (reads the request-scoped <see cref="CourtlyDbContext"/>)
/// over the singleton cache. Only non-expired tokens are cached — expired access tokens are already
/// rejected by lifetime validation, so denylisting them would just bloat the set.
/// </summary>
public sealed class RevokedTokenCache : IRevokedTokenCache
{
    private const string CacheKey = "auth:revoked-jtis";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private readonly IMemoryCache _cache;
    private readonly CourtlyDbContext _db;
    private readonly IClock _clock;

    public RevokedTokenCache(IMemoryCache cache, CourtlyDbContext db, IClock clock)
    {
        _cache = cache;
        _db = db;
        _clock = clock;
    }

    public async Task<bool> IsRevokedAsync(string jti, CancellationToken ct = default)
    {
        var revoked = await GetRevokedSetAsync(ct);
        return revoked.Contains(jti);
    }

    public void Invalidate() => _cache.Remove(CacheKey);

    private async Task<HashSet<string>> GetRevokedSetAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(CacheKey, out HashSet<string>? cached) && cached is not null)
        {
            return cached;
        }

        var now = _clock.UtcNow;
        var jtis = await _db.RevokedTokens
            .AsNoTracking()
            .Where(r => r.ExpiresAtUtc > now)
            .Select(r => r.Jti)
            .ToListAsync(ct);

        var set = new HashSet<string>(jtis, StringComparer.Ordinal);
        _cache.Set(CacheKey, set, CacheTtl);
        return set;
    }
}
