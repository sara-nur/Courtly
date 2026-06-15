using Courtly.Application.Auth;
using Courtly.Domain.Entities;
using Courtly.Infrastructure.Persistence;
using Courtly.Tests.Auth; // TestClock
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Courtly.Tests.Caching;

/// <summary>Feature 6: the per-request jti denylist is served from IMemoryCache and refreshed on
/// invalidation, so a logged-out token is rejected on the next request without a DB hit per call.</summary>
public class RevokedTokenCacheTests
{
    private static CourtlyDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CourtlyDbContext>()
            .UseInMemoryDatabase($"revoked-{Guid.NewGuid()}")
            .Options);

    private static RevokedTokenCache NewCache(CourtlyDbContext db, TestClock clock) =>
        new(new MemoryCache(new MemoryCacheOptions()), db, clock);

    private static RevokedToken Revoked(string jti, TestClock clock) => new()
    {
        UserId = Guid.NewGuid(),
        Jti = jti,
        ExpiresAtUtc = clock.UtcNow.AddMinutes(15),
        RevokedAtUtc = clock.UtcNow,
    };

    [Fact]
    public async Task Revoked_jti_is_reported_revoked_and_others_are_not()
    {
        await using var db = NewDb();
        var clock = new TestClock();
        db.RevokedTokens.Add(Revoked("jti-1", clock));
        await db.SaveChangesAsync();

        var cache = NewCache(db, clock);

        Assert.True(await cache.IsRevokedAsync("jti-1"));
        Assert.False(await cache.IsRevokedAsync("unknown-jti"));
    }

    [Fact]
    public async Task Invalidate_forces_reload_so_a_new_revocation_is_seen()
    {
        await using var db = NewDb();
        var clock = new TestClock();
        var cache = NewCache(db, clock);

        // Warm the (empty) denylist into the cache.
        Assert.False(await cache.IsRevokedAsync("jti-2"));

        db.RevokedTokens.Add(Revoked("jti-2", clock));
        await db.SaveChangesAsync();

        // Still served from the stale cache until it is invalidated...
        Assert.False(await cache.IsRevokedAsync("jti-2"));

        // ...then logout's invalidation makes the next check reload from the DB.
        cache.Invalidate();
        Assert.True(await cache.IsRevokedAsync("jti-2"));
    }
}
