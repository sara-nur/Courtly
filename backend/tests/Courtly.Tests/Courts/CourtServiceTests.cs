using Courtly.Application.Abstractions;
using Courtly.Application.Common.Exceptions;
using Courtly.Application.Courts;
using Courtly.Contracts.Common;
using Courtly.Contracts.Court;
using Courtly.Domain.Entities;
using Courtly.Domain.Enums;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Courtly.Tests.Courts;

/// <summary>
/// Feature 10 DoD (auto): court create + FK validation, the list filters run at the database (Where clause), and
/// delete is restricted while reservations or time slots reference the court. Mirrors the golden Feature 9
/// service harness (EF InMemory provider, fresh DB per test, NullLogger). The search test relies on
/// <see cref="CourtService"/>'s provider guard — under InMemory it uses a lowered <c>Contains</c> instead of the
/// Postgres-only <c>EF.Functions.ILike</c>, so the same test exercises case-insensitive filtering here.
/// </summary>
public class CourtServiceTests
{
    private static CourtlyDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CourtlyDbContext>()
            .UseInMemoryDatabase($"courts-{Guid.NewGuid()}")
            .Options);

    /// <summary>A fixed clock so the maintenance "covering now" projection is deterministic in tests.</summary>
    private static readonly DateTime FixedNow = new(2026, 6, 25, 12, 0, 0, DateTimeKind.Utc);

    private sealed class TestClock : IClock
    {
        public TestClock(DateTime now) => UtcNow = now;
        public DateTime UtcNow { get; }
    }

    private static CourtService NewService(CourtlyDbContext db, DateTime? now = null) =>
        new(db, new TestClock(now ?? FixedNow), NullLogger<CourtService>.Instance);

    /// <summary>An all-null filter; named args override only the dimension under test.</summary>
    private static CourtListQuery Filter(
        string? search = null, long? cityId = null, long? countryId = null, long? surfaceTypeId = null,
        long? courtTypeId = null, bool? isIndoor = null, bool? isActive = null, decimal? minPrice = null,
        decimal? maxPrice = null, bool? isFeatured = null, bool? underMaintenance = null, double? minRating = null) =>
        new(search, cityId, countryId, surfaceTypeId, courtTypeId, isIndoor, isActive, minPrice, maxPrice,
            isFeatured, underMaintenance, minRating);

    private sealed record Refs(long CountryId, long CityId, long SurfaceTypeId, long CourtTypeId);

    private static async Task<Refs> SeedRefsAsync(
        CourtlyDbContext db, string country = "Bosnia", string iso = "BIH",
        string city = "Sarajevo", string surface = "Clay", string courtType = "Tennis")
    {
        var countryEntity = new Country { Name = country, IsoCode = iso };
        db.Countries.Add(countryEntity);
        await db.SaveChangesAsync();

        var cityEntity = new City { Name = city, CountryId = countryEntity.Id };
        db.Cities.Add(cityEntity);
        var surfaceEntity = new SurfaceType { Name = surface };
        db.SurfaceTypes.Add(surfaceEntity);
        var courtTypeEntity = new CourtType { Name = courtType };
        db.CourtTypes.Add(courtTypeEntity);
        await db.SaveChangesAsync();

        return new Refs(countryEntity.Id, cityEntity.Id, surfaceEntity.Id, courtTypeEntity.Id);
    }

    private static CreateCourtRequest NewCourtRequest(
        Refs refs, string name = "Center Court", string? description = "Main show court.",
        bool isIndoor = false, bool isActive = true, bool isFeatured = false, decimal hourlyPrice = 20m,
        double? latitude = null, double? longitude = null) =>
        new(name, description, refs.CityId, refs.SurfaceTypeId, refs.CourtTypeId,
            isIndoor, isActive, isFeatured, hourlyPrice, latitude, longitude);

    // --- Create + FK validation ------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_persists_and_returns_dto_with_joined_names()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        var service = NewService(db);

        var dto = await service.CreateAsync(NewCourtRequest(refs, isFeatured: true, hourlyPrice: 35m));

        Assert.True(dto.Id > 0);
        Assert.Equal("Center Court", dto.Name);
        Assert.Equal(35m, dto.HourlyPrice);
        Assert.True(dto.IsFeatured);
        // FK names are projected via JOINs (no N+1), not stored on the court.
        Assert.Equal("Sarajevo", dto.CityName);
        Assert.Equal("Bosnia", dto.CountryName);
        Assert.Equal("Clay", dto.SurfaceTypeName);
        Assert.Equal("Tennis", dto.CourtTypeName);
        Assert.Null(dto.PrimaryImageUrl); // images are F11
        Assert.Equal(1, await db.Courts.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_trims_name_and_blanks_description_to_null()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        var service = NewService(db);

        var dto = await service.CreateAsync(NewCourtRequest(refs, name: "  Baseline  ", description: "   "));

        Assert.Equal("Baseline", dto.Name);
        Assert.Null(dto.Description);
    }

    [Fact]
    public async Task CreateAsync_persists_lat_lng_and_round_trips_through_the_dto()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        var service = NewService(db);

        var created = await service.CreateAsync(NewCourtRequest(refs, latitude: 43.8563, longitude: 18.4131));

        Assert.Equal(43.8563, created.Latitude);
        Assert.Equal(18.4131, created.Longitude);

        // Re-read via the list/detail projection to prove lat/lng survive the round-trip, not just the post-write read.
        var refetched = await service.GetByIdAsync(created.Id);
        Assert.Equal(43.8563, refetched.Latitude);
        Assert.Equal(18.4131, refetched.Longitude);

        // Update can move and then clear the location (both-or-neither is enforced by the validator, not here).
        var moved = await service.UpdateAsync(created.Id, new UpdateCourtRequest(
            created.Name, created.Description, refs.CityId, refs.SurfaceTypeId, refs.CourtTypeId,
            created.IsIndoor, created.IsActive, created.IsFeatured, created.HourlyPrice,
            Latitude: 45.0, Longitude: 16.0));
        Assert.Equal(45.0, moved.Latitude);
        Assert.Equal(16.0, moved.Longitude);
    }

    [Fact]
    public async Task CreateAsync_missing_city_throws_NotFound()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        var service = NewService(db);

        var request = NewCourtRequest(refs) with { CityId = 999 };
        await Assert.ThrowsAsync<NotFoundException>(() => service.CreateAsync(request));
    }

    [Fact]
    public async Task CreateAsync_missing_surface_type_throws_NotFound()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        var service = NewService(db);

        var request = NewCourtRequest(refs) with { SurfaceTypeId = 999 };
        await Assert.ThrowsAsync<NotFoundException>(() => service.CreateAsync(request));
    }

    [Fact]
    public async Task CreateAsync_missing_court_type_throws_NotFound()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        var service = NewService(db);

        var request = NewCourtRequest(refs) with { CourtTypeId = 999 };
        await Assert.ThrowsAsync<NotFoundException>(() => service.CreateAsync(request));
    }

    [Fact]
    public async Task GetByIdAsync_missing_throws_NotFound()
    {
        await using var db = NewDb();
        var service = NewService(db);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetByIdAsync(999));
    }

    // --- List filtering (every filter runs at the DB) --------------------------------------------

    [Fact]
    public async Task GetPagedAsync_search_filters_by_name_case_insensitively()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        db.Courts.AddRange(
            new Court { Name = "Center Court", CityId = refs.CityId, SurfaceTypeId = refs.SurfaceTypeId, CourtTypeId = refs.CourtTypeId },
            new Court { Name = "Baseline", CityId = refs.CityId, SurfaceTypeId = refs.SurfaceTypeId, CourtTypeId = refs.CourtTypeId },
            new Court { Name = "North Arena", CityId = refs.CityId, SurfaceTypeId = refs.SurfaceTypeId, CourtTypeId = refs.CourtTypeId });
        await db.SaveChangesAsync();
        var service = NewService(db);

        var page = await service.GetPagedAsync(new PaginationQuery(), Filter(search: "center"));

        var only = Assert.Single(page.Items);
        Assert.Equal("Center Court", only.Name);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task GetPagedAsync_filters_by_surface_type()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        var grass = new SurfaceType { Name = "Grass" };
        db.SurfaceTypes.Add(grass);
        await db.SaveChangesAsync();
        db.Courts.AddRange(
            new Court { Name = "Clay 1", CityId = refs.CityId, SurfaceTypeId = refs.SurfaceTypeId, CourtTypeId = refs.CourtTypeId },
            new Court { Name = "Grass 1", CityId = refs.CityId, SurfaceTypeId = grass.Id, CourtTypeId = refs.CourtTypeId });
        await db.SaveChangesAsync();
        var service = NewService(db);

        var page = await service.GetPagedAsync(new PaginationQuery(), Filter(surfaceTypeId: refs.SurfaceTypeId));

        var only = Assert.Single(page.Items);
        Assert.Equal("Clay 1", only.Name);
        Assert.Equal("Clay", only.SurfaceTypeName);
    }

    [Fact]
    public async Task GetPagedAsync_filters_by_court_type()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        var padel = new CourtType { Name = "Padel" };
        db.CourtTypes.Add(padel);
        await db.SaveChangesAsync();
        db.Courts.AddRange(
            new Court { Name = "Tennis 1", CityId = refs.CityId, SurfaceTypeId = refs.SurfaceTypeId, CourtTypeId = refs.CourtTypeId },
            new Court { Name = "Padel 1", CityId = refs.CityId, SurfaceTypeId = refs.SurfaceTypeId, CourtTypeId = padel.Id });
        await db.SaveChangesAsync();
        var service = NewService(db);

        var page = await service.GetPagedAsync(new PaginationQuery(), Filter(courtTypeId: padel.Id));

        var only = Assert.Single(page.Items);
        Assert.Equal("Padel 1", only.Name);
    }

    [Fact]
    public async Task GetPagedAsync_filters_by_indoor()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        db.Courts.AddRange(
            new Court { Name = "Indoor 1", CityId = refs.CityId, SurfaceTypeId = refs.SurfaceTypeId, CourtTypeId = refs.CourtTypeId, IsIndoor = true },
            new Court { Name = "Outdoor 1", CityId = refs.CityId, SurfaceTypeId = refs.SurfaceTypeId, CourtTypeId = refs.CourtTypeId, IsIndoor = false });
        await db.SaveChangesAsync();
        var service = NewService(db);

        var page = await service.GetPagedAsync(new PaginationQuery(), Filter(isIndoor: true));

        var only = Assert.Single(page.Items);
        Assert.Equal("Indoor 1", only.Name);
    }

    [Fact]
    public async Task GetPagedAsync_filters_by_price_range()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        db.Courts.AddRange(
            new Court { Name = "Cheap", CityId = refs.CityId, SurfaceTypeId = refs.SurfaceTypeId, CourtTypeId = refs.CourtTypeId, HourlyPrice = 10m },
            new Court { Name = "Mid", CityId = refs.CityId, SurfaceTypeId = refs.SurfaceTypeId, CourtTypeId = refs.CourtTypeId, HourlyPrice = 25m },
            new Court { Name = "Expensive", CityId = refs.CityId, SurfaceTypeId = refs.SurfaceTypeId, CourtTypeId = refs.CourtTypeId, HourlyPrice = 50m });
        await db.SaveChangesAsync();
        var service = NewService(db);

        var page = await service.GetPagedAsync(new PaginationQuery(), Filter(minPrice: 20m, maxPrice: 30m));

        var only = Assert.Single(page.Items);
        Assert.Equal("Mid", only.Name);
    }

    [Fact]
    public async Task GetPagedAsync_filters_by_city()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        var mostar = new City { Name = "Mostar", CountryId = refs.CountryId };
        db.Cities.Add(mostar);
        await db.SaveChangesAsync();
        db.Courts.AddRange(
            new Court { Name = "Sarajevo Court", CityId = refs.CityId, SurfaceTypeId = refs.SurfaceTypeId, CourtTypeId = refs.CourtTypeId },
            new Court { Name = "Mostar Court", CityId = mostar.Id, SurfaceTypeId = refs.SurfaceTypeId, CourtTypeId = refs.CourtTypeId });
        await db.SaveChangesAsync();
        var service = NewService(db);

        var page = await service.GetPagedAsync(new PaginationQuery(), Filter(cityId: mostar.Id));

        var only = Assert.Single(page.Items);
        Assert.Equal("Mostar Court", only.Name);
    }

    [Fact]
    public async Task GetPagedAsync_filters_by_country_through_city()
    {
        await using var db = NewDb();
        var bosnia = await SeedRefsAsync(db);
        var croatia = new Country { Name = "Croatia", IsoCode = "HRV" };
        db.Countries.Add(croatia);
        await db.SaveChangesAsync();
        var zagreb = new City { Name = "Zagreb", CountryId = croatia.Id };
        db.Cities.Add(zagreb);
        await db.SaveChangesAsync();
        db.Courts.AddRange(
            new Court { Name = "Sarajevo Court", CityId = bosnia.CityId, SurfaceTypeId = bosnia.SurfaceTypeId, CourtTypeId = bosnia.CourtTypeId },
            new Court { Name = "Zagreb Court", CityId = zagreb.Id, SurfaceTypeId = bosnia.SurfaceTypeId, CourtTypeId = bosnia.CourtTypeId });
        await db.SaveChangesAsync();
        var service = NewService(db);

        var page = await service.GetPagedAsync(new PaginationQuery(), Filter(countryId: croatia.Id));

        var only = Assert.Single(page.Items);
        Assert.Equal("Zagreb Court", only.Name);
        Assert.Equal("Croatia", only.CountryName);
    }

    [Fact]
    public async Task GetPagedAsync_filters_by_featured()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        db.Courts.AddRange(
            new Court { Name = "Featured", CityId = refs.CityId, SurfaceTypeId = refs.SurfaceTypeId, CourtTypeId = refs.CourtTypeId, IsFeatured = true },
            new Court { Name = "Plain", CityId = refs.CityId, SurfaceTypeId = refs.SurfaceTypeId, CourtTypeId = refs.CourtTypeId, IsFeatured = false });
        await db.SaveChangesAsync();
        var service = NewService(db);

        var page = await service.GetPagedAsync(new PaginationQuery(), Filter(isFeatured: true));

        var only = Assert.Single(page.Items);
        Assert.Equal("Featured", only.Name);
    }

    [Fact]
    public async Task GetPagedAsync_orders_newest_first()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        var service = NewService(db);
        await service.CreateAsync(NewCourtRequest(refs, name: "First"));
        await service.CreateAsync(NewCourtRequest(refs, name: "Second"));
        await service.CreateAsync(NewCourtRequest(refs, name: "Third"));

        var page = await service.GetPagedAsync(new PaginationQuery(), Filter());

        Assert.Equal(3, page.TotalCount);
        Assert.Equal("Third", page.Items[0].Name); // highest id first
    }

    // --- Maintenance projection + filter (F12) ---------------------------------------------------

    [Fact]
    public async Task GetByIdAsync_marks_court_under_maintenance_when_an_open_window_covers_now()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        var court = new Court { Name = "Center Court", CityId = refs.CityId, SurfaceTypeId = refs.SurfaceTypeId, CourtTypeId = refs.CourtTypeId };
        db.Courts.Add(court);
        await db.SaveChangesAsync();
        db.CourtMaintenanceLogs.Add(new CourtMaintenanceLog
        {
            CourtId = court.Id,
            Status = MaintenanceStatus.InProgress,
            Reason = "Resurfacing",
            StartUtc = FixedNow.AddHours(-1),
            EndUtc = null,
            CreatedAtUtc = FixedNow.AddHours(-1),
        });
        await db.SaveChangesAsync();
        var service = NewService(db);

        var dto = await service.GetByIdAsync(court.Id);

        Assert.True(dto.IsUnderMaintenance);
        Assert.Equal("Resurfacing", dto.MaintenanceReason);
        Assert.Equal(FixedNow.AddHours(-1), dto.MaintenanceStartUtc);
    }

    [Fact]
    public async Task GetByIdAsync_not_under_maintenance_when_window_is_terminal_or_future()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        var court = new Court { Name = "Center Court", CityId = refs.CityId, SurfaceTypeId = refs.SurfaceTypeId, CourtTypeId = refs.CourtTypeId };
        db.Courts.Add(court);
        await db.SaveChangesAsync();
        db.CourtMaintenanceLogs.AddRange(
            // Completed (terminal) window in the past → does not count.
            new CourtMaintenanceLog { CourtId = court.Id, Status = MaintenanceStatus.Completed, Reason = "Old fix", StartUtc = FixedNow.AddDays(-2), EndUtc = FixedNow.AddDays(-2).AddHours(2), CreatedAtUtc = FixedNow.AddDays(-2) },
            // Scheduled window starting tomorrow → not covering now.
            new CourtMaintenanceLog { CourtId = court.Id, Status = MaintenanceStatus.Scheduled, Reason = "Future", StartUtc = FixedNow.AddDays(1), EndUtc = null, CreatedAtUtc = FixedNow });
        await db.SaveChangesAsync();
        var service = NewService(db);

        var dto = await service.GetByIdAsync(court.Id);

        Assert.False(dto.IsUnderMaintenance);
        Assert.Null(dto.MaintenanceReason);
    }

    [Fact]
    public async Task GetPagedAsync_filters_by_under_maintenance()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        var down = new Court { Name = "Down", CityId = refs.CityId, SurfaceTypeId = refs.SurfaceTypeId, CourtTypeId = refs.CourtTypeId };
        var up = new Court { Name = "Up", CityId = refs.CityId, SurfaceTypeId = refs.SurfaceTypeId, CourtTypeId = refs.CourtTypeId };
        db.Courts.AddRange(down, up);
        await db.SaveChangesAsync();
        db.CourtMaintenanceLogs.Add(new CourtMaintenanceLog
        {
            CourtId = down.Id,
            Status = MaintenanceStatus.InProgress,
            Reason = "Lights out",
            StartUtc = FixedNow.AddHours(-1),
            CreatedAtUtc = FixedNow.AddHours(-1),
        });
        await db.SaveChangesAsync();
        var service = NewService(db);

        var maint = await service.GetPagedAsync(new PaginationQuery(), Filter(underMaintenance: true));
        var available = await service.GetPagedAsync(new PaginationQuery(), Filter(underMaintenance: false));

        Assert.Equal("Down", Assert.Single(maint.Items).Name);
        Assert.Equal("Up", Assert.Single(available.Items).Name);
    }

    // --- Update ----------------------------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_changes_fields_and_returns_updated_dto()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        var service = NewService(db);
        var created = await service.CreateAsync(NewCourtRequest(refs, name: "Old", hourlyPrice: 20m, isActive: true));

        var updated = await service.UpdateAsync(created.Id, new UpdateCourtRequest(
            "New Name", "Updated.", refs.CityId, refs.SurfaceTypeId, refs.CourtTypeId,
            IsIndoor: true, IsActive: false, IsFeatured: true, HourlyPrice: 42m));

        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("New Name", updated.Name);
        Assert.Equal(42m, updated.HourlyPrice);
        Assert.True(updated.IsIndoor);
        Assert.False(updated.IsActive);
        Assert.True(updated.IsFeatured);
    }

    [Fact]
    public async Task UpdateAsync_missing_throws_NotFound()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        var service = NewService(db);

        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateAsync(
            999, new UpdateCourtRequest("X", null, refs.CityId, refs.SurfaceTypeId, refs.CourtTypeId,
                false, true, false, 10m)));
    }

    // --- Delete-restrict -------------------------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_throws_Business_when_a_reservation_references_the_court()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        var court = new Court { Name = "Center Court", CityId = refs.CityId, SurfaceTypeId = refs.SurfaceTypeId, CourtTypeId = refs.CourtTypeId };
        db.Courts.Add(court);
        await db.SaveChangesAsync();
        db.Reservations.Add(new Reservation
        {
            UserId = Guid.NewGuid(),
            CourtId = court.Id,
            TimeSlotId = 1,
            TotalPrice = 20m,
            CreatedAtUtc = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
        });
        await db.SaveChangesAsync();
        var service = NewService(db);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.DeleteAsync(court.Id));
        Assert.Contains("Center Court", ex.Message);   // reason names the court
        Assert.Contains("reservations", ex.Message);    // and the referencing relation
        Assert.Equal(1, await db.Courts.CountAsync());   // not deleted
    }

    [Fact]
    public async Task DeleteAsync_throws_Business_when_a_time_slot_references_the_court()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        var court = new Court { Name = "Center Court", CityId = refs.CityId, SurfaceTypeId = refs.SurfaceTypeId, CourtTypeId = refs.CourtTypeId };
        db.Courts.Add(court);
        await db.SaveChangesAsync();
        db.TimeSlots.Add(new TimeSlot
        {
            CourtId = court.Id,
            StartUtc = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            EndUtc = new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc),
            Price = 20m,
            IsActive = true,
        });
        await db.SaveChangesAsync();
        var service = NewService(db);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.DeleteAsync(court.Id));
        Assert.Contains("Center Court", ex.Message);
        Assert.Contains("time slots", ex.Message);
        Assert.Equal(1, await db.Courts.CountAsync()); // not deleted
    }

    [Fact]
    public async Task DeleteAsync_removes_an_unreferenced_court()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        var service = NewService(db);
        var created = await service.CreateAsync(NewCourtRequest(refs));

        await service.DeleteAsync(created.Id);

        Assert.Equal(0, await db.Courts.CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_missing_throws_NotFound()
    {
        await using var db = NewDb();
        var service = NewService(db);

        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(999));
    }

    // --- Rating aggregate projection + MinRating filter (F23) -------------------------------------

    /// <summary>Adds a review row for a court. FK to a reservation is not enforced under the InMemory provider, so the
    /// rating-only signal is seeded directly without a backing reservation.</summary>
    private static void AddReview(CourtlyDbContext db, long courtId, int rating) =>
        db.Reviews.Add(new Review
        {
            CourtId = courtId,
            UserId = Guid.NewGuid(),
            Rating = rating,
            CreatedAtUtc = FixedNow,
        });

    /// <summary>Seeds one reviewed court (ratings 4 + 5 → avg 4.5, count 2) and one court with no reviews.</summary>
    private static async Task<(long ReviewedId, long UnratedId)> SeedRatedAndUnratedAsync(CourtlyDbContext db, Refs refs)
    {
        var reviewed = new Court { Name = "Reviewed", CityId = refs.CityId, SurfaceTypeId = refs.SurfaceTypeId, CourtTypeId = refs.CourtTypeId };
        var unrated = new Court { Name = "Unrated", CityId = refs.CityId, SurfaceTypeId = refs.SurfaceTypeId, CourtTypeId = refs.CourtTypeId };
        db.Courts.AddRange(reviewed, unrated);
        await db.SaveChangesAsync();

        AddReview(db, reviewed.Id, 4);
        AddReview(db, reviewed.Id, 5);
        await db.SaveChangesAsync();

        return (reviewed.Id, unrated.Id);
    }

    [Fact]
    public async Task GetPagedAsync_projects_average_rating_and_count_per_court()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        var (reviewedId, unratedId) = await SeedRatedAndUnratedAsync(db, refs);
        var service = NewService(db);

        var page = await service.GetPagedAsync(new PaginationQuery(), Filter());

        var reviewed = page.Items.Single(c => c.Id == reviewedId);
        Assert.Equal(4.5, reviewed.AvgRating);
        Assert.Equal(2, reviewed.ReviewCount);

        var unrated = page.Items.Single(c => c.Id == unratedId);
        Assert.Null(unrated.AvgRating);  // no reviews → null average, not 0
        Assert.Equal(0, unrated.ReviewCount);
    }

    [Fact]
    public async Task GetPagedAsync_filters_by_min_rating_and_excludes_unrated_courts()
    {
        await using var db = NewDb();
        var refs = await SeedRefsAsync(db);
        var (reviewedId, _) = await SeedRatedAndUnratedAsync(db, refs);
        var service = NewService(db);

        // MinRating 5 is above the 4.5 average → the reviewed court is excluded (and so is the unrated one).
        var aboveAvg = await service.GetPagedAsync(new PaginationQuery(), Filter(minRating: 5));
        Assert.Empty(aboveAvg.Items);

        // MinRating 4 is at/below the 4.5 average → the reviewed court is included; the unrated court is still excluded
        // because an unrated court never satisfies a rating floor.
        var atAvg = await service.GetPagedAsync(new PaginationQuery(), Filter(minRating: 4));
        var only = Assert.Single(atAvg.Items);
        Assert.Equal(reviewedId, only.Id);
        Assert.Equal("Reviewed", only.Name);
    }
}
