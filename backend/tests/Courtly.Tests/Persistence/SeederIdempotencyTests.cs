using Courtly.Domain.Entities;
using Courtly.Infrastructure.Persistence;
using Courtly.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Courtly.Tests.Persistence;

/// <summary>
/// Feature 4 DoD (auto): proves the seed is idempotent and the seeded credentials are usable. Runs against the
/// EF in-memory provider: <c>EnsureCreated</c> applies the HasData half (roles + reference data) and the runtime
/// <see cref="CourtlyDataSeeder"/> adds the dynamic half. Re-running the seeder must not duplicate any rows.
/// </summary>
public class SeederIdempotencyTests
{
    private static CourtlyDbContext NewContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<CourtlyDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new CourtlyDbContext(options);
    }

    private static ICourtlyDataSeeder NewSeeder(CourtlyDbContext db) => new CourtlyDataSeeder(
        db,
        new PasswordHasher<AppUser>(),
        new SeedImageLoader(NullLogger<SeedImageLoader>.Instance),
        NullLogger<CourtlyDataSeeder>.Instance);

    // Counts of every table the runtime seeder writes, in a fixed order.
    private static async Task<long[]> SnapshotAsync(CourtlyDbContext db) => new[]
    {
        await db.Users.LongCountAsync(),
        await db.UserRoles.LongCountAsync(),
        await db.Courts.LongCountAsync(),
        await db.CourtImages.LongCountAsync(),
        await db.CourtAmenities.LongCountAsync(),
        await db.CourtMaintenanceLogs.LongCountAsync(),
        await db.TimeSlots.LongCountAsync(),
        await db.Reservations.LongCountAsync(),
        await db.ReservationAudits.LongCountAsync(),
        await db.Payments.LongCountAsync(),
        await db.Reviews.LongCountAsync(),
        await db.Notifications.LongCountAsync(),
        await db.News.LongCountAsync(),
    };

    [Fact]
    public async Task Seeding_Twice_Does_Not_Duplicate_Rows()
    {
        var dbName = $"seed-idem-{Guid.NewGuid()}";

        long[] afterFirst;
        await using (var db = NewContext(dbName))
        {
            await db.Database.EnsureCreatedAsync(); // applies HasData: roles + reference data
            await NewSeeder(db).SeedAsync();
            afterFirst = await SnapshotAsync(db);
        }

        long[] afterSecond;
        await using (var db = NewContext(dbName))
        {
            await NewSeeder(db).SeedAsync(); // re-run against the same database
            afterSecond = await SnapshotAsync(db);
        }

        Assert.Equal(afterFirst, afterSecond);

        // Sanity: the first run actually populated the expected demo data (indices match SnapshotAsync order).
        Assert.Equal(4, afterFirst[0]);  // users
        Assert.Equal(4, afterFirst[1]);  // user-role links
        Assert.Equal(6, afterFirst[2]);  // courts
        Assert.Equal(1, afterFirst[5]);  // maintenance logs
        Assert.Equal(5, afterFirst[7]);  // reservations
        Assert.Equal(3, afterFirst[9]);  // payments
        Assert.Equal(2, afterFirst[10]); // reviews
        Assert.Equal(2, afterFirst[12]); // news
    }

    [Fact]
    public async Task HasData_Seeds_Roles_And_Reference_Data()
    {
        var dbName = $"seed-ref-{Guid.NewGuid()}";
        await using var db = NewContext(dbName);
        await db.Database.EnsureCreatedAsync();

        Assert.Equal(3, await db.Roles.LongCountAsync());
        Assert.Equal(1, await db.Countries.LongCountAsync());
        Assert.Equal(3, await db.Cities.LongCountAsync());
        Assert.Equal(4, await db.SurfaceTypes.LongCountAsync());
        Assert.Equal(4, await db.CourtTypes.LongCountAsync());
        Assert.Equal(5, await db.Amenities.LongCountAsync());
    }

    [Fact]
    public async Task Seeded_Credentials_Verify_With_Identity_Hasher()
    {
        var dbName = $"seed-creds-{Guid.NewGuid()}";
        await using var db = NewContext(dbName);
        await db.Database.EnsureCreatedAsync();
        await NewSeeder(db).SeedAsync();

        var admin = await db.Users.SingleAsync(u => u.UserName == "desktop");
        Assert.False(string.IsNullOrWhiteSpace(admin.PasswordHash));

        var hasher = new PasswordHasher<AppUser>();
        Assert.Equal(
            PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(admin, admin.PasswordHash!, "test"));
        Assert.Equal(
            PasswordVerificationResult.Failed,
            hasher.VerifyHashedPassword(admin, admin.PasswordHash!, "wrong-password"));
    }
}
