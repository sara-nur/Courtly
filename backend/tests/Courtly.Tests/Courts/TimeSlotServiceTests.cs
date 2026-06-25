using Courtly.Application.Abstractions;
using Courtly.Application.Common.Exceptions;
using Courtly.Application.Courts.Maintenance;
using Courtly.Application.Courts.Slots;
using Courtly.Contracts.Court;
using Courtly.Domain.Entities;
using Courtly.Domain.Enums;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Courtly.Tests.Courts;

/// <summary>
/// Feature 13 DoD (auto): the slot service generates the court's bookable slots with the server-owned bucket + price
/// (incl. the optional evening peak), is idempotent over an overlapping range (duplicate starts skipped), answers the
/// per-day availability (free/taken, bucketed) — yielding NO slots while the court is under maintenance (feature-12
/// exclusion) — and removes slots with the booked-slot guard. Mirrors the F12 service harness (EF InMemory, fresh DB
/// per test, NullLogger, fixed <see cref="IClock"/>). The real <see cref="MaintenanceService"/> is wired in so the
/// maintenance→no-slots integration is exercised end-to-end.
/// </summary>
public class TimeSlotServiceTests
{
    private const decimal HourlyPrice = 20m;
    private static readonly DateTime Now = new(2026, 6, 25, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now);

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow { get; init; } = Now;
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public Guid? UserId { get; init; }
        public string? Email => null;
        public string? Jti => null;
        public DateTime? AccessTokenExpiresAtUtc => null;
        public bool IsAuthenticated => UserId.HasValue;
        public IReadOnlyList<string> Roles => Array.Empty<string>();
        public bool IsInRole(string role) => false;
    }

    private static CourtlyDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CourtlyDbContext>()
            .UseInMemoryDatabase($"slots-{Guid.NewGuid()}")
            .Options);

    private static TimeSlotService NewService(CourtlyDbContext db, DateTime? now = null)
    {
        var clock = new TestClock { UtcNow = now ?? Now };
        var maintenance = new MaintenanceService(
            db, clock, new TestCurrentUser(), NullLogger<MaintenanceService>.Instance);
        return new TimeSlotService(db, clock, maintenance, NullLogger<TimeSlotService>.Instance);
    }

    private static MaintenanceService NewMaintenance(CourtlyDbContext db) =>
        new(db, new TestClock(), new TestCurrentUser(), NullLogger<MaintenanceService>.Instance);

    /// <summary>Seeds the FK chain + one active court (with <see cref="HourlyPrice"/>), returning its id.</summary>
    private static async Task<long> SeedCourtAsync(CourtlyDbContext db)
    {
        var country = new Country { Name = "Bosnia", IsoCode = "BIH" };
        db.Countries.Add(country);
        await db.SaveChangesAsync();
        var city = new City { Name = "Sarajevo", CountryId = country.Id };
        var surface = new SurfaceType { Name = "Clay" };
        var courtType = new CourtType { Name = "Tennis" };
        db.Cities.Add(city);
        db.SurfaceTypes.Add(surface);
        db.CourtTypes.Add(courtType);
        await db.SaveChangesAsync();
        var court = new Court
        {
            Name = "Center Court",
            CityId = city.Id,
            SurfaceTypeId = surface.Id,
            CourtTypeId = courtType.Id,
            IsActive = true,
            HourlyPrice = HourlyPrice,
        };
        db.Courts.Add(court);
        await db.SaveChangesAsync();
        return court.Id;
    }

    private static GenerateSlotsRequest Request(
        int openHour = 8, int closeHour = 20, int slotMinutes = 60, decimal? peak = null,
        DateOnly? from = null, DateOnly? to = null) =>
        new(from ?? Today, to ?? Today, openHour, closeHour, slotMinutes, peak);

    /// <summary>Id of the slot starting at <paramref name="hour"/>:00 UTC today — built the same way the service does
    /// (<c>ToUtcMidnight + hours</c>) so the instants match exactly.</summary>
    private static Task<long> SlotIdAtHourAsync(CourtlyDbContext db, long courtId, int hour)
    {
        var start = new DateTime(Now.Year, Now.Month, Now.Day, hour, 0, 0, DateTimeKind.Utc);
        return db.TimeSlots.Where(s => s.CourtId == courtId && s.StartUtc == start).Select(s => s.Id).FirstAsync();
    }

    private static async Task AddReservationAsync(
        CourtlyDbContext db, long courtId, long slotId, ReservationStatus status)
    {
        var userId = Guid.NewGuid();
        db.Users.Add(new AppUser
        {
            Id = userId,
            FirstName = "Test",
            LastName = "User",
            Email = $"{userId:N}@courtly.test",
            UserName = userId.ToString("N"),
            IsActive = true,
            CreatedAtUtc = Now,
        });
        db.Reservations.Add(new Reservation
        {
            UserId = userId,
            CourtId = courtId,
            TimeSlotId = slotId,
            Status = status,
            TotalPrice = HourlyPrice,
            CreatedAtUtc = Now,
        });
        await db.SaveChangesAsync();
    }

    private static IReadOnlyList<AvailabilitySlotDto> Bucket(DayAvailabilityDto day, TimeOfDayBucket bucket) =>
        day.Buckets.Single(b => b.Bucket == bucket).Slots;

    // --- Generation -----------------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_creates_hourly_slots_with_correct_buckets_and_price()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);

        var result = await service.GenerateAsync(courtId, Request()); // 08..20, hourly

        Assert.Equal(12, result.CreatedCount); // starts 08..19
        Assert.Equal(0, result.SkippedCount);

        var day = await service.GetDayAvailabilityAsync(courtId, Today);
        Assert.Equal(4, Bucket(day, TimeOfDayBucket.Morning).Count);   // 8,9,10,11
        Assert.Equal(5, Bucket(day, TimeOfDayBucket.Afternoon).Count); // 12..16
        Assert.Equal(3, Bucket(day, TimeOfDayBucket.Evening).Count);   // 17,18,19

        // Server-owned price: flat hourly for non-evening, default 1.2× peak for evening.
        Assert.Equal(20.00m, Bucket(day, TimeOfDayBucket.Morning)[0].Price);
        Assert.Equal(24.00m, Bucket(day, TimeOfDayBucket.Evening)[0].Price);
    }

    [Fact]
    public async Task GenerateAsync_custom_evening_peak_applies_to_evening_only()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);

        await service.GenerateAsync(courtId, Request(peak: 1.5m));

        var day = await service.GetDayAvailabilityAsync(courtId, Today);
        Assert.Equal(20.00m, Bucket(day, TimeOfDayBucket.Afternoon)[0].Price); // unchanged
        Assert.Equal(30.00m, Bucket(day, TimeOfDayBucket.Evening)[0].Price);   // 20 × 1.5
    }

    [Fact]
    public async Task GenerateAsync_slot_minutes_scales_count_and_price()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);

        // 08:00–09:00, 30-min slots → 08:00 and 08:30, each priced for half an hour.
        var result = await service.GenerateAsync(courtId, Request(openHour: 8, closeHour: 9, slotMinutes: 30));

        Assert.Equal(2, result.CreatedCount);
        var day = await service.GetDayAvailabilityAsync(courtId, Today);
        var morning = Bucket(day, TimeOfDayBucket.Morning);
        Assert.Equal(2, morning.Count);
        Assert.Equal(10.00m, morning[0].Price); // 20 × 0.5h
    }

    [Fact]
    public async Task GenerateAsync_skips_existing_starts_on_regenerate()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);

        var first = await service.GenerateAsync(courtId, Request());
        var second = await service.GenerateAsync(courtId, Request()); // same range

        Assert.Equal(12, first.CreatedCount);
        Assert.Equal(0, second.CreatedCount);
        Assert.Equal(12, second.SkippedCount);
        Assert.Equal(12, await db.TimeSlots.CountAsync(s => s.CourtId == courtId)); // no duplicates
    }

    [Fact]
    public async Task GenerateAsync_missing_court_throws_NotFound()
    {
        await using var db = NewDb();
        var service = NewService(db);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GenerateAsync(999, Request()));
    }

    // --- Availability ---------------------------------------------------------------------------

    [Fact]
    public async Task GetDayAvailabilityAsync_flags_taken_slots()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);
        await service.GenerateAsync(courtId, Request());

        var bookedSlotId = await SlotIdAtHourAsync(db, courtId, 14);
        await AddReservationAsync(db, courtId, bookedSlotId, ReservationStatus.Confirmed);

        var day = await service.GetDayAvailabilityAsync(courtId, Today);
        var afternoon = Bucket(day, TimeOfDayBucket.Afternoon);
        Assert.True(afternoon.Single(s => s.Id == bookedSlotId).IsTaken);
        Assert.All(afternoon.Where(s => s.Id != bookedSlotId), s => Assert.False(s.IsTaken));
    }

    [Fact]
    public async Task GetDayAvailabilityAsync_court_under_maintenance_yields_no_slots()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);
        await service.GenerateAsync(courtId, Request());

        // Put the court under maintenance now (open-ended) → it covers today.
        await NewMaintenance(db).CreateAsync(courtId, new CreateMaintenanceRequest("Resurfacing"));

        var day = await service.GetDayAvailabilityAsync(courtId, Today);
        Assert.True(day.IsCourtUnderMaintenance);
        Assert.All(day.Buckets, b => Assert.Empty(b.Slots));
    }

    [Fact]
    public async Task GetDayAvailabilityAsync_missing_court_throws_NotFound()
    {
        await using var db = NewDb();
        var service = NewService(db);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetDayAvailabilityAsync(999, Today));
    }

    // --- Removal --------------------------------------------------------------------------------

    [Fact]
    public async Task RemoveSlotAsync_unbooked_slot_is_hard_deleted()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);
        await service.GenerateAsync(courtId, Request());
        var slotId = await SlotIdAtHourAsync(db, courtId, 9);

        await service.RemoveSlotAsync(courtId, slotId);

        Assert.False(await db.TimeSlots.AnyAsync(s => s.Id == slotId));
    }

    [Fact]
    public async Task RemoveSlotAsync_active_booking_throws_Business()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);
        await service.GenerateAsync(courtId, Request());
        var slotId = await SlotIdAtHourAsync(db, courtId, 10);
        await AddReservationAsync(db, courtId, slotId, ReservationStatus.Confirmed);

        await Assert.ThrowsAsync<BusinessException>(() => service.RemoveSlotAsync(courtId, slotId));
        Assert.True(await db.TimeSlots.AnyAsync(s => s.Id == slotId)); // untouched
    }

    [Fact]
    public async Task RemoveSlotAsync_historical_reservation_soft_removes()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);
        await service.GenerateAsync(courtId, Request());
        var slotId = await SlotIdAtHourAsync(db, courtId, 11);
        await AddReservationAsync(db, courtId, slotId, ReservationStatus.Cancelled);

        await service.RemoveSlotAsync(courtId, slotId);

        // Row preserved (FK history) but deactivated → gone from availability.
        Assert.False((await db.TimeSlots.FirstAsync(s => s.Id == slotId)).IsActive);
        var day = await service.GetDayAvailabilityAsync(courtId, Today);
        Assert.DoesNotContain(day.Buckets.SelectMany(b => b.Slots), s => s.Id == slotId);
    }

    [Fact]
    public async Task RemoveDayAsync_removes_free_slots_and_keeps_booked()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);
        await service.GenerateAsync(courtId, Request()); // 12 slots
        var bookedSlotId = await SlotIdAtHourAsync(db, courtId, 18);
        await AddReservationAsync(db, courtId, bookedSlotId, ReservationStatus.Pending);

        var result = await service.RemoveDayAsync(courtId, Today);

        Assert.Equal(11, result.RemovedCount);
        Assert.Equal(1, result.BlockedCount);
        // Only the actively-booked slot remains bookable.
        var day = await service.GetDayAvailabilityAsync(courtId, Today);
        var remaining = day.Buckets.SelectMany(b => b.Slots).ToList();
        Assert.Single(remaining);
        Assert.Equal(bookedSlotId, remaining[0].Id);
        Assert.True(remaining[0].IsTaken);
    }
}
