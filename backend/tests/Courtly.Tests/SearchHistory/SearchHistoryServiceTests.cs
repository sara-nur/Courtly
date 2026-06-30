using Courtly.Application.Abstractions;
using Courtly.Application.SearchHistory;
using Courtly.Contracts.SearchHistory;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Courtly.Tests.SearchHistory;

/// <summary>
/// Feature 23 DoD (auto): the search-history capture writes one recommender-signal row per distinct search owned by
/// the JWT caller, stamps a UTC timestamp itself, drops an identical search repeated inside the dedupe window, and is
/// a silent no-op for an unauthenticated caller. Mirrors the golden service harness (EF InMemory provider, fresh DB
/// per test, NullLogger) and the <c>ReservationServiceTests</c> fake <see cref="ICurrentUser"/>.
/// </summary>
public class SearchHistoryServiceTests
{
    private sealed class TestCurrentUser : ICurrentUser
    {
        public Guid? UserId { get; init; }
        public string? Email => null;
        public string? Jti => null;
        public DateTime? AccessTokenExpiresAtUtc => null;
        public bool IsAuthenticated => UserId.HasValue;
        public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
        public bool IsInRole(string role) => Roles.Contains(role);
    }

    /// <summary>A live pass-through clock so the "timestamp is now" assertion stays honest (dedupe only needs a stable now within the call).</summary>
    private sealed class RealClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }

    private static CourtlyDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CourtlyDbContext>()
            .UseInMemoryDatabase($"search-history-{Guid.NewGuid()}")
            .Options);

    private static SearchHistoryService NewService(CourtlyDbContext db, Guid? userId = null) =>
        new(db, new TestCurrentUser { UserId = userId }, new RealClock(), NullLogger<SearchHistoryService>.Instance);

    /// <summary>An all-null search; named args override only the dimension under test.</summary>
    private static RecordSearchRequest Search(
        long? surfaceTypeId = null, long? courtTypeId = null, decimal? minPrice = null,
        decimal? maxPrice = null, bool? indoorOnly = null, string? rawQuery = null) =>
        new(surfaceTypeId, courtTypeId, minPrice, maxPrice, indoorOnly, rawQuery);

    [Fact]
    public async Task RecordAsync_writes_a_row_with_the_request_fields_owner_and_utc_timestamp()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var service = NewService(db, userId);
        var before = DateTime.UtcNow;

        await service.RecordAsync(Search(
            surfaceTypeId: 3, courtTypeId: 7, minPrice: 10m, maxPrice: 40m, indoorOnly: true, rawQuery: "clay courts"));

        var row = Assert.Single(await db.SearchHistories.AsNoTracking().ToListAsync());
        Assert.Equal(userId, row.UserId);            // owner from ICurrentUser, never the body
        Assert.Equal(3, row.SurfaceTypeId);
        Assert.Equal(7, row.CourtTypeId);
        Assert.Equal(10m, row.MinPrice);
        Assert.Equal(40m, row.MaxPrice);
        Assert.True(row.IndoorOnly);
        Assert.Equal("clay courts", row.RawQuery);
        Assert.Null(row.Bucket);                      // derived later, not supplied by the client
        Assert.Equal(DateTimeKind.Utc, row.CreatedAtUtc.Kind);
        Assert.InRange(row.CreatedAtUtc, before, DateTime.UtcNow);
    }

    [Fact]
    public async Task RecordAsync_dedupes_an_identical_search_within_the_window()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var service = NewService(db, userId);

        await service.RecordAsync(Search(surfaceTypeId: 3, courtTypeId: 7, rawQuery: "clay"));
        await service.RecordAsync(Search(surfaceTypeId: 3, courtTypeId: 7, rawQuery: "clay"));

        // The second identical call lands inside the dedupe window, so no second row is written.
        Assert.Equal(1, await db.SearchHistories.CountAsync());
    }

    [Fact]
    public async Task RecordAsync_writes_a_new_row_when_a_signal_differs()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();
        var service = NewService(db, userId);

        await service.RecordAsync(Search(surfaceTypeId: 3, courtTypeId: 7, rawQuery: "clay"));
        await service.RecordAsync(Search(surfaceTypeId: 4, courtTypeId: 7, rawQuery: "clay")); // different surface

        Assert.Equal(2, await db.SearchHistories.CountAsync());
    }

    [Fact]
    public async Task RecordAsync_is_a_no_op_when_the_caller_is_unauthenticated()
    {
        await using var db = NewDb();
        var service = NewService(db, userId: null); // ICurrentUser.UserId is null

        await service.RecordAsync(Search(surfaceTypeId: 3, courtTypeId: 7, rawQuery: "clay"));

        Assert.Equal(0, await db.SearchHistories.CountAsync());
    }
}
