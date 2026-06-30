using Courtly.Application.Abstractions;
using Courtly.Application.Courts.Maintenance;
using Courtly.Application.Dashboard;
using Courtly.Contracts.Dashboard;
using Courtly.Domain.Entities;
using Courtly.Domain.Enums;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Courtly.Tests.Dashboard;

/// <summary>
/// Feature 19 DoD (auto): each dashboard aggregate is one GroupBy/Count/Sum with the correct numbers versus seeded
/// data. Mirrors the F14 service harness — EF InMemory, fresh DB per test, fixed <see cref="IClock"/>, the real
/// <see cref="MaintenanceService"/> so the maintenance-exclusion path runs end-to-end, and a real
/// <see cref="MemoryCache"/>. Covers: which reservations count (Confirmed/Completed only), net-of-refunds revenue,
/// occupancy ratio, distinct active users, peak-hour bucketing, top-N popular courts, the court-type filter,
/// maintenance exclusion, the prior-period delta, the health rule, and the short-TTL cache.
/// </summary>
public class DashboardServiceTests
{
    // "Now" sits inside the default 30-day window, which is [2026-05-26 12:00, 2026-06-25 12:00).
    private static readonly DateTime Now = new(2026, 6, 25, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime InWindow = new(2026, 6, 10, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime InPriorWindow = new(2026, 5, 10, 10, 0, 0, DateTimeKind.Utc);

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow { get; init; } = Now;
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string? Email => null;
        public string? Jti => null;
        public DateTime? AccessTokenExpiresAtUtc => null;
        public bool IsAuthenticated => false;
        public IReadOnlyList<string> Roles => Array.Empty<string>();
        public bool IsInRole(string role) => false;
    }

    private static CourtlyDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CourtlyDbContext>()
            .UseInMemoryDatabase($"dashboard-{Guid.NewGuid()}")
            .Options);

    private static DashboardService NewService(CourtlyDbContext db, IMemoryCache? cache = null, DateTime? now = null)
    {
        var clock = new TestClock { UtcNow = now ?? Now };
        var maintenance = new MaintenanceService(db, clock, new TestCurrentUser(), NullLogger<MaintenanceService>.Instance);
        return new DashboardService(
            db, clock, maintenance, cache ?? new MemoryCache(new MemoryCacheOptions()),
            NullLogger<DashboardService>.Instance);
    }

    // --- seed helpers -------------------------------------------------------------------------------

    private sealed record Fixture(long CityId, long SurfaceId);

    private static async Task<Fixture> SeedRefDataAsync(CourtlyDbContext db)
    {
        var country = new Country { Name = "Bosnia", IsoCode = "BIH" };
        db.Countries.Add(country);
        await db.SaveChangesAsync();
        var city = new City { Name = "Sarajevo", CountryId = country.Id };
        var surface = new SurfaceType { Name = "Clay" };
        db.Cities.Add(city);
        db.SurfaceTypes.Add(surface);
        await db.SaveChangesAsync();
        return new Fixture(city.Id, surface.Id);
    }

    private static async Task<long> SeedCourtTypeAsync(CourtlyDbContext db, string name)
    {
        var courtType = new CourtType { Name = name };
        db.CourtTypes.Add(courtType);
        await db.SaveChangesAsync();
        return courtType.Id;
    }

    private static async Task<long> SeedCourtAsync(
        CourtlyDbContext db, Fixture fx, long courtTypeId, string name, bool active = true)
    {
        var court = new Court
        {
            Name = name,
            CityId = fx.CityId,
            SurfaceTypeId = fx.SurfaceId,
            CourtTypeId = courtTypeId,
            IsActive = active,
            HourlyPrice = 30m,
        };
        db.Courts.Add(court);
        await db.SaveChangesAsync();
        return court.Id;
    }

    private static async Task<long> SeedSlotAsync(
        CourtlyDbContext db, long courtId, DateTime startUtc, bool active = true)
    {
        var slot = new TimeSlot
        {
            CourtId = courtId,
            StartUtc = startUtc,
            EndUtc = startUtc.AddHours(1),
            Price = 30m,
            Bucket = TimeOfDayBucket.Morning,
            IsActive = active,
        };
        db.TimeSlots.Add(slot);
        await db.SaveChangesAsync();
        return slot.Id;
    }

    private static async Task<Guid> SeedUserAsync(CourtlyDbContext db, string suffix)
    {
        var id = Guid.NewGuid();
        db.Users.Add(new AppUser
        {
            Id = id,
            FirstName = "Test",
            LastName = suffix,
            Email = $"{suffix}@courtly.test",
            UserName = suffix,
            IsActive = true,
            CreatedAtUtc = Now,
        });
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>Inserts a reservation (and an optional Succeeded/Refunded payment) directly. <paramref name="createdAtUtc"/>
    /// drives the volume/active-user metrics; the slot's start drives occupancy/peak-hours/popular-courts.</summary>
    private static async Task<long> SeedReservationAsync(
        CourtlyDbContext db, long courtId, long slotId, Guid userId, ReservationStatus status, DateTime createdAtUtc,
        PaymentStatus? paymentStatus = null, long chargedCents = 3000, DateTime? paidAtUtc = null)
    {
        var reservation = new Reservation
        {
            CourtId = courtId,
            TimeSlotId = slotId,
            UserId = userId,
            Status = status,
            TotalPrice = chargedCents / 100m,
            CreatedAtUtc = createdAtUtc,
        };
        db.Reservations.Add(reservation);
        await db.SaveChangesAsync();

        if (paymentStatus is { } ps)
        {
            var paid = paidAtUtc ?? createdAtUtc;
            var payment = new Payment
            {
                ReservationId = reservation.Id,
                Status = ps,
                Amount = chargedCents / 100m,
                AmountChargedCents = chargedCents,
                PaidAtUtc = paid,
                CreatedAtUtc = paid,
            };
            db.Payments.Add(payment);
            await db.SaveChangesAsync();

            if (ps == PaymentStatus.Refunded)
            {
                db.Refunds.Add(new Refund
                {
                    PaymentId = payment.Id,
                    Status = RefundStatus.Succeeded,
                    Amount = chargedCents / 100m,
                    CreatedAtUtc = paid,
                });
                await db.SaveChangesAsync();
            }
        }

        return reservation.Id;
    }

    private static async Task SeedMaintenanceAsync(CourtlyDbContext db, long courtId, DateTime startUtc)
    {
        db.CourtMaintenanceLogs.Add(new CourtMaintenanceLog
        {
            CourtId = courtId,
            Status = MaintenanceStatus.InProgress,
            Reason = "Resurfacing",
            StartUtc = startUtc,
            EndUtc = null,
            CreatedAtUtc = startUtc,
        });
        await db.SaveChangesAsync();
    }

    private static DashboardFiltersQuery NoFilters => new();

    // --- tests --------------------------------------------------------------------------------------

    [Fact]
    public async Task TotalReservations_counts_only_confirmed_and_completed()
    {
        await using var db = NewDb();
        var fx = await SeedRefDataAsync(db);
        var type = await SeedCourtTypeAsync(db, "Tennis");
        var court = await SeedCourtAsync(db, fx, type, "Center");
        var user = await SeedUserAsync(db, "u1");

        foreach (var status in new[]
                 {
                     ReservationStatus.Confirmed, ReservationStatus.Completed,
                     ReservationStatus.Pending, ReservationStatus.Cancelled,
                 })
        {
            var slot = await SeedSlotAsync(db, court, InWindow.AddHours(status == ReservationStatus.Completed ? 1 : 0));
            await SeedReservationAsync(db, court, slot, user, status, InWindow);
        }

        var metrics = await NewService(db).GetMetricsAsync(NoFilters);

        // Confirmed + Completed only — Pending and Cancelled never count.
        Assert.Equal(2m, metrics.TotalReservations.Current);
    }

    [Fact]
    public async Task Revenue_is_net_of_refunds()
    {
        await using var db = NewDb();
        var fx = await SeedRefDataAsync(db);
        var type = await SeedCourtTypeAsync(db, "Tennis");
        var court = await SeedCourtAsync(db, fx, type, "Center");
        var user = await SeedUserAsync(db, "u1");

        var s1 = await SeedSlotAsync(db, court, InWindow);
        var s2 = await SeedSlotAsync(db, court, InWindow.AddHours(2));
        var s3 = await SeedSlotAsync(db, court, InWindow.AddHours(4));
        await SeedReservationAsync(db, court, s1, user, ReservationStatus.Confirmed, InWindow, PaymentStatus.Succeeded, 3000);
        await SeedReservationAsync(db, court, s2, user, ReservationStatus.Completed, InWindow, PaymentStatus.Succeeded, 4500);
        // A refunded payment is excluded (status flipped to Refunded) — it must not subtract from or add to revenue.
        await SeedReservationAsync(db, court, s3, user, ReservationStatus.Cancelled, InWindow, PaymentStatus.Refunded, 9999);

        var metrics = await NewService(db).GetMetricsAsync(NoFilters);

        Assert.Equal(75m, metrics.Revenue.Current); // (3000 + 4500) / 100
    }

    [Fact]
    public async Task OccupancyRate_is_booked_over_available_slots()
    {
        await using var db = NewDb();
        var fx = await SeedRefDataAsync(db);
        var type = await SeedCourtTypeAsync(db, "Tennis");
        var court = await SeedCourtAsync(db, fx, type, "Center");
        var user = await SeedUserAsync(db, "u1");

        // 4 active slots in the window; 1 booked by a counted reservation → 25%.
        var booked = await SeedSlotAsync(db, court, InWindow);
        await SeedSlotAsync(db, court, InWindow.AddHours(1));
        await SeedSlotAsync(db, court, InWindow.AddHours(2));
        await SeedSlotAsync(db, court, InWindow.AddHours(3));
        await SeedReservationAsync(db, court, booked, user, ReservationStatus.Confirmed, InWindow);

        var metrics = await NewService(db).GetMetricsAsync(NoFilters);

        Assert.Equal(25m, metrics.OccupancyRate.Current);
    }

    [Fact]
    public async Task ActiveUsers_counts_distinct_bookers()
    {
        await using var db = NewDb();
        var fx = await SeedRefDataAsync(db);
        var type = await SeedCourtTypeAsync(db, "Tennis");
        var court = await SeedCourtAsync(db, fx, type, "Center");
        var u1 = await SeedUserAsync(db, "u1");
        var u2 = await SeedUserAsync(db, "u2");

        // u1 books twice, u2 once → 2 distinct active users.
        var s1 = await SeedSlotAsync(db, court, InWindow);
        var s2 = await SeedSlotAsync(db, court, InWindow.AddHours(1));
        var s3 = await SeedSlotAsync(db, court, InWindow.AddHours(2));
        await SeedReservationAsync(db, court, s1, u1, ReservationStatus.Confirmed, InWindow);
        await SeedReservationAsync(db, court, s2, u1, ReservationStatus.Completed, InWindow);
        await SeedReservationAsync(db, court, s3, u2, ReservationStatus.Confirmed, InWindow);

        var metrics = await NewService(db).GetMetricsAsync(NoFilters);

        Assert.Equal(2m, metrics.ActiveUsers.Current);
    }

    [Fact]
    public async Task PeakHours_buckets_by_slot_start_hour_and_fills_all_24()
    {
        await using var db = NewDb();
        var fx = await SeedRefDataAsync(db);
        var type = await SeedCourtTypeAsync(db, "Tennis");
        var court = await SeedCourtAsync(db, fx, type, "Center");
        var user = await SeedUserAsync(db, "u1");

        var at10a = await SeedSlotAsync(db, court, new DateTime(2026, 6, 10, 10, 0, 0, DateTimeKind.Utc));
        var at10b = await SeedSlotAsync(db, court, new DateTime(2026, 6, 11, 10, 0, 0, DateTimeKind.Utc));
        var at18 = await SeedSlotAsync(db, court, new DateTime(2026, 6, 12, 18, 0, 0, DateTimeKind.Utc));
        await SeedReservationAsync(db, court, at10a, user, ReservationStatus.Confirmed, InWindow);
        await SeedReservationAsync(db, court, at10b, user, ReservationStatus.Completed, InWindow);
        await SeedReservationAsync(db, court, at18, user, ReservationStatus.Confirmed, InWindow);

        var metrics = await NewService(db).GetMetricsAsync(NoFilters);

        Assert.Equal(24, metrics.PeakHours.Count);
        Assert.Equal(2, metrics.PeakHours.Single(p => p.Hour == 10).Count);
        Assert.Equal(1, metrics.PeakHours.Single(p => p.Hour == 18).Count);
        Assert.Equal(0, metrics.PeakHours.Single(p => p.Hour == 0).Count);
    }

    [Fact]
    public async Task PopularCourts_ranks_courts_by_count_with_percentages()
    {
        await using var db = NewDb();
        var fx = await SeedRefDataAsync(db);
        var type = await SeedCourtTypeAsync(db, "Tennis");
        var busy = await SeedCourtAsync(db, fx, type, "Busy Court");
        var quiet = await SeedCourtAsync(db, fx, type, "Quiet Court");
        var user = await SeedUserAsync(db, "u1");

        for (var i = 0; i < 3; i++)
        {
            var slot = await SeedSlotAsync(db, busy, InWindow.AddHours(i));
            await SeedReservationAsync(db, busy, slot, user, ReservationStatus.Confirmed, InWindow);
        }

        var quietSlot = await SeedSlotAsync(db, quiet, InWindow);
        await SeedReservationAsync(db, quiet, quietSlot, user, ReservationStatus.Confirmed, InWindow);

        var metrics = await NewService(db).GetMetricsAsync(NoFilters);

        Assert.Equal(2, metrics.PopularCourts.Count);
        Assert.Equal("Busy Court", metrics.PopularCourts[0].CourtName);
        Assert.Equal(3, metrics.PopularCourts[0].Count);
        Assert.Equal(75d, metrics.PopularCourts[0].Percentage); // 3 of 4 total counted reservations
        Assert.Equal("Quiet Court", metrics.PopularCourts[1].CourtName);
        Assert.Equal(25d, metrics.PopularCourts[1].Percentage);
    }

    [Fact]
    public async Task Maintenance_courts_are_excluded_from_every_figure()
    {
        await using var db = NewDb();
        var fx = await SeedRefDataAsync(db);
        var type = await SeedCourtTypeAsync(db, "Tennis");
        var normal = await SeedCourtAsync(db, fx, type, "Open Court");
        var down = await SeedCourtAsync(db, fx, type, "Closed Court");
        var user = await SeedUserAsync(db, "u1");

        var openSlot = await SeedSlotAsync(db, normal, InWindow);
        await SeedReservationAsync(db, normal, openSlot, user, ReservationStatus.Confirmed, InWindow, PaymentStatus.Succeeded, 3000);

        var downSlot = await SeedSlotAsync(db, down, InWindow.AddHours(1));
        await SeedReservationAsync(db, down, downSlot, user, ReservationStatus.Confirmed, InWindow, PaymentStatus.Succeeded, 5000);
        await SeedMaintenanceAsync(db, down, InWindow); // open window → court excluded from analytics

        var metrics = await NewService(db).GetMetricsAsync(NoFilters);

        Assert.Equal(1m, metrics.TotalReservations.Current);  // only the open court
        Assert.Equal(30m, metrics.Revenue.Current);           // only the open court's 3000 cents
        Assert.Single(metrics.PopularCourts);
        Assert.Equal("Open Court", metrics.PopularCourts[0].CourtName);
    }

    [Fact]
    public async Task CourtType_filter_narrows_to_the_selected_type()
    {
        await using var db = NewDb();
        var fx = await SeedRefDataAsync(db);
        var tennis = await SeedCourtTypeAsync(db, "Tennis");
        var padel = await SeedCourtTypeAsync(db, "Padel");
        var tennisCourt = await SeedCourtAsync(db, fx, tennis, "Tennis Court");
        var padelCourt = await SeedCourtAsync(db, fx, padel, "Padel Court");
        var user = await SeedUserAsync(db, "u1");

        var ts = await SeedSlotAsync(db, tennisCourt, InWindow);
        var ps = await SeedSlotAsync(db, padelCourt, InWindow.AddHours(1));
        await SeedReservationAsync(db, tennisCourt, ts, user, ReservationStatus.Confirmed, InWindow);
        await SeedReservationAsync(db, padelCourt, ps, user, ReservationStatus.Confirmed, InWindow);

        var svc = NewService(db);
        var all = await svc.GetMetricsAsync(NoFilters);
        var tennisOnly = await svc.GetMetricsAsync(new DashboardFiltersQuery(CourtTypeId: tennis));

        Assert.Equal(2m, all.TotalReservations.Current);
        Assert.Equal(1m, tennisOnly.TotalReservations.Current);
        Assert.Single(tennisOnly.PopularCourts);
        Assert.Equal("Tennis Court", tennisOnly.PopularCourts[0].CourtName);
    }

    [Fact]
    public async Task PriorPeriod_delta_is_computed_against_the_previous_window()
    {
        await using var db = NewDb();
        var fx = await SeedRefDataAsync(db);
        var type = await SeedCourtTypeAsync(db, "Tennis");
        var court = await SeedCourtAsync(db, fx, type, "Center");
        var user = await SeedUserAsync(db, "u1");

        // 4 in the current window, 2 in the prior window → +100%.
        for (var i = 0; i < 4; i++)
        {
            var slot = await SeedSlotAsync(db, court, InWindow.AddHours(i));
            await SeedReservationAsync(db, court, slot, user, ReservationStatus.Confirmed, InWindow);
        }

        for (var i = 0; i < 2; i++)
        {
            var slot = await SeedSlotAsync(db, court, InPriorWindow.AddHours(i));
            await SeedReservationAsync(db, court, slot, user, ReservationStatus.Confirmed, InPriorWindow);
        }

        var metrics = await NewService(db).GetMetricsAsync(NoFilters);

        Assert.Equal(4m, metrics.TotalReservations.Current);
        Assert.Equal(2m, metrics.TotalReservations.Previous);
        Assert.Equal(100d, metrics.TotalReservations.DeltaPercent);
    }

    [Fact]
    public async Task Health_is_AtRisk_when_occupancy_is_low()
    {
        await using var db = NewDb();
        var fx = await SeedRefDataAsync(db);
        var type = await SeedCourtTypeAsync(db, "Tennis");
        var court = await SeedCourtAsync(db, fx, type, "Center");
        var user = await SeedUserAsync(db, "u1");

        // 5 active slots, 1 booked → 20% occupancy (< 30% threshold).
        var booked = await SeedSlotAsync(db, court, InWindow);
        for (var i = 1; i < 5; i++)
        {
            await SeedSlotAsync(db, court, InWindow.AddHours(i));
        }
        await SeedReservationAsync(db, court, booked, user, ReservationStatus.Confirmed, InWindow);

        var metrics = await NewService(db).GetMetricsAsync(NoFilters);

        Assert.Equal(DashboardHealthStatus.AtRisk, metrics.Health.Status);
    }

    [Fact]
    public async Task Health_is_Healthy_when_occupancy_is_strong()
    {
        await using var db = NewDb();
        var fx = await SeedRefDataAsync(db);
        var type = await SeedCourtTypeAsync(db, "Tennis");
        var court = await SeedCourtAsync(db, fx, type, "Center");
        var user = await SeedUserAsync(db, "u1");

        // Both active slots booked → 100% occupancy; some revenue, no prior-period drop.
        var s1 = await SeedSlotAsync(db, court, InWindow);
        var s2 = await SeedSlotAsync(db, court, InWindow.AddHours(1));
        await SeedReservationAsync(db, court, s1, user, ReservationStatus.Confirmed, InWindow, PaymentStatus.Succeeded, 3000);
        await SeedReservationAsync(db, court, s2, user, ReservationStatus.Completed, InWindow, PaymentStatus.Succeeded, 3000);

        var metrics = await NewService(db).GetMetricsAsync(NoFilters);

        Assert.Equal(DashboardHealthStatus.Healthy, metrics.Health.Status);
    }

    [Fact]
    public async Task Metrics_are_cached_for_the_same_filters()
    {
        await using var db = NewDb();
        var fx = await SeedRefDataAsync(db);
        var type = await SeedCourtTypeAsync(db, "Tennis");
        var court = await SeedCourtAsync(db, fx, type, "Center");
        var user = await SeedUserAsync(db, "u1");

        var slot = await SeedSlotAsync(db, court, InWindow);
        await SeedReservationAsync(db, court, slot, user, ReservationStatus.Confirmed, InWindow);

        var cache = new MemoryCache(new MemoryCacheOptions());
        var svc = NewService(db, cache);
        var first = await svc.GetMetricsAsync(NoFilters);

        // Mutate the DB after the first (now-cached) read; the short-TTL cache must serve the same snapshot.
        var slot2 = await SeedSlotAsync(db, court, InWindow.AddHours(1));
        await SeedReservationAsync(db, court, slot2, user, ReservationStatus.Confirmed, InWindow);

        var second = await svc.GetMetricsAsync(NoFilters);

        Assert.Equal(1m, first.TotalReservations.Current);
        Assert.Equal(first.TotalReservations.Current, second.TotalReservations.Current);
    }
}
