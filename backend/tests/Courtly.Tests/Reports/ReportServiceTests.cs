using System.Text;
using Courtly.Application.Abstractions;
using Courtly.Application.Courts.Maintenance;
using Courtly.Application.Dashboard;
using Courtly.Application.Reports;
using Courtly.Contracts.Dashboard;
using Courtly.Contracts.Reports;
using Courtly.Domain.Entities;
using Courtly.Domain.Enums;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Courtly.Tests.Reports;

/// <summary>
/// Feature 20 DoD (auto): each report endpoint produces a valid, non-empty PDF for given filters, and the
/// revenue/utilisation figures reconcile with the F19 dashboard for the same window (they share
/// <see cref="Courtly.Application.Analytics.AnalyticsFilters"/>). Mirrors the F19 harness — EF InMemory, fresh DB per
/// test, fixed <see cref="IClock"/>, the real <see cref="MaintenanceService"/> so maintenance exclusion runs
/// end-to-end. Also covers: the reservations report lists every status, the status filter narrows it, and a court
/// under maintenance is excluded from the revenue report.
/// </summary>
public class ReportServiceTests
{
    private static readonly DateTime Now = new(2026, 6, 25, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime InWindow = new(2026, 6, 10, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime MonthStart = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime MonthEnd = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

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
            .UseInMemoryDatabase($"reports-{Guid.NewGuid()}")
            .Options);

    private static ReportService NewService(CourtlyDbContext db)
    {
        var clock = new TestClock();
        var maintenance = new MaintenanceService(db, clock, new TestCurrentUser(), NullLogger<MaintenanceService>.Instance);
        return new ReportService(db, clock, maintenance, NullLogger<ReportService>.Instance);
    }

    private static DashboardService NewDashboard(CourtlyDbContext db)
    {
        var clock = new TestClock();
        var maintenance = new MaintenanceService(db, clock, new TestCurrentUser(), NullLogger<MaintenanceService>.Instance);
        return new DashboardService(
            db, clock, maintenance, new MemoryCache(new MemoryCacheOptions()), NullLogger<DashboardService>.Instance);
    }

    // --- seed helpers -------------------------------------------------------------------------------

    private sealed record Fixture(long CityId, long SurfaceId, long CourtTypeId);

    private static async Task<Fixture> SeedRefDataAsync(CourtlyDbContext db)
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
        return new Fixture(city.Id, surface.Id, courtType.Id);
    }

    private static async Task<long> SeedCourtAsync(CourtlyDbContext db, Fixture fx, string name, bool active = true)
    {
        var court = new Court
        {
            Name = name,
            CityId = fx.CityId,
            SurfaceTypeId = fx.SurfaceId,
            CourtTypeId = fx.CourtTypeId,
            IsActive = active,
            HourlyPrice = 30m,
        };
        db.Courts.Add(court);
        await db.SaveChangesAsync();
        return court.Id;
    }

    private static async Task<long> SeedSlotAsync(CourtlyDbContext db, long courtId, DateTime startUtc, bool active = true)
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

    private static async Task SeedReservationAsync(
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
            db.Payments.Add(new Payment
            {
                ReservationId = reservation.Id,
                Status = ps,
                Amount = chargedCents / 100m,
                AmountChargedCents = chargedCents,
                PaidAtUtc = paid,
                CreatedAtUtc = paid,
            });
            await db.SaveChangesAsync();
        }
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

    private static bool IsPdf(byte[] bytes) =>
        bytes.Length > 4 && Encoding.ASCII.GetString(bytes, 0, 4) == "%PDF";

    // --- tests --------------------------------------------------------------------------------------

    [Fact]
    public async Task Reservations_report_returns_a_valid_non_empty_pdf()
    {
        await using var db = NewDb();
        var fx = await SeedRefDataAsync(db);
        var court = await SeedCourtAsync(db, fx, "Center");
        var user = await SeedUserAsync(db, "u1");
        var slot = await SeedSlotAsync(db, court, InWindow);
        await SeedReservationAsync(db, court, slot, user, ReservationStatus.Confirmed, InWindow, PaymentStatus.Succeeded);

        var pdf = await NewService(db).GenerateReservationsReportAsync(new ReservationsReportQuery());

        Assert.NotEmpty(pdf);
        Assert.True(IsPdf(pdf), "Reservations report should be a valid PDF (starts with %PDF).");
    }

    [Fact]
    public async Task Revenue_report_returns_a_valid_non_empty_pdf()
    {
        await using var db = NewDb();
        var fx = await SeedRefDataAsync(db);
        var court = await SeedCourtAsync(db, fx, "Center");
        var user = await SeedUserAsync(db, "u1");
        var slot = await SeedSlotAsync(db, court, InWindow);
        await SeedReservationAsync(db, court, slot, user, ReservationStatus.Confirmed, InWindow, PaymentStatus.Succeeded);

        var pdf = await NewService(db).GenerateRevenueUtilizationReportAsync(
            new RevenueUtilizationReportQuery(Year: 2026, Month: 6));

        Assert.NotEmpty(pdf);
        Assert.True(IsPdf(pdf), "Revenue report should be a valid PDF (starts with %PDF).");
    }

    [Fact]
    public async Task Reservations_report_lists_every_status_and_uses_RES_reference()
    {
        await using var db = NewDb();
        var fx = await SeedRefDataAsync(db);
        var court = await SeedCourtAsync(db, fx, "Center");
        var user = await SeedUserAsync(db, "u1");

        foreach (var status in new[]
                 {
                     ReservationStatus.Confirmed, ReservationStatus.Pending, ReservationStatus.Cancelled,
                 })
        {
            var slot = await SeedSlotAsync(db, court, InWindow.AddHours((int)status));
            await SeedReservationAsync(db, court, slot, user, status, InWindow);
        }

        var data = await NewService(db).BuildReservationsDataAsync(new ReservationsReportQuery());

        // Unlike the dashboard, the operational listing keeps Pending and Cancelled.
        Assert.Equal(3, data.TotalCount);
        Assert.Equal(3, data.StatusBreakdown.Sum(s => s.Count));
        Assert.All(data.Rows, r => Assert.StartsWith("#RES-", r.Reference));
    }

    [Fact]
    public async Task Reservations_report_status_filter_narrows_results()
    {
        await using var db = NewDb();
        var fx = await SeedRefDataAsync(db);
        var court = await SeedCourtAsync(db, fx, "Center");
        var user = await SeedUserAsync(db, "u1");

        var s1 = await SeedSlotAsync(db, court, InWindow);
        var s2 = await SeedSlotAsync(db, court, InWindow.AddHours(1));
        await SeedReservationAsync(db, court, s1, user, ReservationStatus.Confirmed, InWindow);
        await SeedReservationAsync(db, court, s2, user, ReservationStatus.Pending, InWindow);

        var pendingOnly = await NewService(db).BuildReservationsDataAsync(
            new ReservationsReportQuery(Status: ReservationStatus.Pending));

        Assert.Equal(1, pendingOnly.TotalCount);
        Assert.Equal("Pending", Assert.Single(pendingOnly.Rows).StatusName);
    }

    [Fact]
    public async Task Revenue_report_reconciles_with_the_dashboard_for_the_same_month()
    {
        await using var db = NewDb();
        var fx = await SeedRefDataAsync(db);
        var court = await SeedCourtAsync(db, fx, "Center");
        var user = await SeedUserAsync(db, "u1");

        // 4 active June slots; 2 booked + paid → 50% utilisation, $75 revenue.
        var s1 = await SeedSlotAsync(db, court, InWindow);
        var s2 = await SeedSlotAsync(db, court, InWindow.AddHours(1));
        await SeedSlotAsync(db, court, InWindow.AddHours(2));
        await SeedSlotAsync(db, court, InWindow.AddHours(3));
        await SeedReservationAsync(db, court, s1, user, ReservationStatus.Confirmed, InWindow, PaymentStatus.Succeeded, 3000);
        await SeedReservationAsync(db, court, s2, user, ReservationStatus.Completed, InWindow, PaymentStatus.Succeeded, 4500);

        var report = await NewService(db).BuildRevenueUtilizationDataAsync(
            new RevenueUtilizationReportQuery(Year: 2026, Month: 6));
        var dashboard = await NewDashboard(db).GetMetricsAsync(
            new DashboardFiltersQuery(FromUtc: MonthStart, ToUtc: MonthEnd));

        Assert.Equal(75m, report.TotalRevenue);
        Assert.Equal(dashboard.Revenue.Current, report.TotalRevenue);
        Assert.Equal((double)dashboard.OccupancyRate.Current, report.OverallUtilizationPct, 1);
    }

    [Fact]
    public async Task Revenue_report_excludes_maintenance_courts()
    {
        await using var db = NewDb();
        var fx = await SeedRefDataAsync(db);
        var open = await SeedCourtAsync(db, fx, "Open Court");
        var down = await SeedCourtAsync(db, fx, "Closed Court");
        var user = await SeedUserAsync(db, "u1");

        var openSlot = await SeedSlotAsync(db, open, InWindow);
        await SeedReservationAsync(db, open, openSlot, user, ReservationStatus.Confirmed, InWindow, PaymentStatus.Succeeded, 3000);

        var downSlot = await SeedSlotAsync(db, down, InWindow.AddHours(1));
        await SeedReservationAsync(db, down, downSlot, user, ReservationStatus.Confirmed, InWindow, PaymentStatus.Succeeded, 5000);
        await SeedMaintenanceAsync(db, down, InWindow); // open window → excluded from the analytics figures

        var report = await NewService(db).BuildRevenueUtilizationDataAsync(
            new RevenueUtilizationReportQuery(Year: 2026, Month: 6));

        Assert.Equal(30m, report.TotalRevenue);              // only the open court's 3000 cents
        Assert.Equal("Open Court", Assert.Single(report.Rows).CourtName);
    }
}
