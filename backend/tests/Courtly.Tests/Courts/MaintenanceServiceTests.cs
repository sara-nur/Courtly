using Courtly.Application.Abstractions;
using Courtly.Application.Common.Exceptions;
using Courtly.Application.Courts.Maintenance;
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
/// Feature 12 DoD (auto): the maintenance service drives windows through the centralized state machine, computes
/// the "under maintenance now" predicate + the analytics/booking exclusion query, rejects overlapping open windows,
/// and records who/when/why. Mirrors the F10/F11 service harness (EF InMemory, fresh DB per test, NullLogger) with a
/// fixed <see cref="IClock"/> so timing is deterministic and a fake <see cref="ICurrentUser"/> so the actor comes
/// from the "JWT", never the route/body.
/// </summary>
public class MaintenanceServiceTests
{
    private static readonly DateTime Now = new(2026, 6, 25, 12, 0, 0, DateTimeKind.Utc);

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
            .UseInMemoryDatabase($"maint-{Guid.NewGuid()}")
            .Options);

    private static MaintenanceService NewService(
        CourtlyDbContext db, Guid? actorId = null, DateTime? now = null) =>
        new(db, new TestClock { UtcNow = now ?? Now }, new TestCurrentUser { UserId = actorId },
            NullLogger<MaintenanceService>.Instance);

    /// <summary>Seeds the FK chain + one court, returning its id. Optionally seeds the acting admin user.</summary>
    private static async Task<long> SeedCourtAsync(CourtlyDbContext db, Guid? actorId = null)
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
        if (actorId.HasValue)
        {
            db.Users.Add(new AppUser
            {
                Id = actorId.Value,
                FirstName = "Super",
                LastName = "Admin",
                Email = "desktop@courtly.test",
                UserName = "desktop",
                IsActive = true,
                CreatedAtUtc = Now,
            });
        }
        await db.SaveChangesAsync();
        var court = new Court { Name = "Center Court", CityId = city.Id, SurfaceTypeId = surface.Id, CourtTypeId = courtType.Id };
        db.Courts.Add(court);
        await db.SaveChangesAsync();
        return court.Id;
    }

    // --- Create ---------------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_with_no_start_puts_the_court_under_maintenance_now()
    {
        await using var db = NewDb();
        var actor = Guid.NewGuid();
        var courtId = await SeedCourtAsync(db, actor);
        var service = NewService(db, actor);

        var dto = await service.CreateAsync(courtId, new CreateMaintenanceRequest("Resurfacing the clay"));

        Assert.Equal(MaintenanceStatus.InProgress, dto.Status);
        Assert.Equal("In Progress", dto.StatusName);
        Assert.Equal(Now, dto.StartUtc);
        Assert.Null(dto.EndUtc);
        Assert.Equal("Super Admin", dto.PerformedByName); // who — resolved from the JWT actor, JOINed by name
        Assert.True(await service.IsCourtUnderMaintenanceAsync(courtId));
    }

    [Fact]
    public async Task CreateAsync_with_a_future_start_schedules_the_window()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);

        var dto = await service.CreateAsync(
            courtId, new CreateMaintenanceRequest("Planned", StartUtc: Now.AddDays(3), EndUtc: Now.AddDays(3).AddHours(4)));

        Assert.Equal(MaintenanceStatus.Scheduled, dto.Status);
        Assert.Equal(Now.AddDays(3), dto.StartUtc);
        // A future window does not make the court unavailable right now.
        Assert.False(await service.IsCourtUnderMaintenanceAsync(courtId));
    }

    [Fact]
    public async Task CreateAsync_end_before_start_throws_Validation()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);

        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(
            courtId, new CreateMaintenanceRequest("Bad window", StartUtc: Now.AddDays(2), EndUtc: Now.AddDays(1))));
    }

    [Fact]
    public async Task CreateAsync_overlapping_open_window_throws_Business()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);
        await service.CreateAsync(courtId, new CreateMaintenanceRequest("First (open-ended, now)"));

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(
            courtId, new CreateMaintenanceRequest("Second overlaps")));
        Assert.Contains("overlap", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_missing_court_throws_NotFound()
    {
        await using var db = NewDb();
        var service = NewService(db);

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.CreateAsync(999, new CreateMaintenanceRequest("x")));
    }

    // --- Transitions (state machine) ------------------------------------------------------------

    [Fact]
    public async Task StartAsync_moves_a_scheduled_window_to_in_progress()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);
        var scheduled = await service.CreateAsync(
            courtId, new CreateMaintenanceRequest("Planned", StartUtc: Now.AddDays(1)));

        var started = await service.StartAsync(courtId, scheduled.Id);

        Assert.Equal(MaintenanceStatus.InProgress, started.Status);
        // Started EARLY (planned for tomorrow, started now) → StartUtc moves to now so the court is unavailable now.
        Assert.Equal(Now, started.StartUtc);
        Assert.True(await service.IsCourtUnderMaintenanceAsync(courtId));
    }

    [Fact]
    public async Task StartAsync_on_or_after_planned_start_preserves_the_planned_StartUtc()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        // Schedule (at "Now") a window that begins in one hour.
        var plannedStart = Now.AddHours(1);
        var scheduled = await NewService(db, now: Now)
            .CreateAsync(courtId, new CreateMaintenanceRequest("Planned", StartUtc: plannedStart));

        // Start it once its planned time has arrived (clock advanced past the planned start).
        var started = await NewService(db, now: Now.AddHours(2)).StartAsync(courtId, scheduled.Id);

        Assert.Equal(MaintenanceStatus.InProgress, started.Status);
        Assert.Equal(plannedStart, started.StartUtc); // planned start preserved (not rewritten to "now")
    }

    [Fact]
    public async Task StartAsync_on_an_in_progress_window_throws_Business()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);
        var open = await service.CreateAsync(courtId, new CreateMaintenanceRequest("Already running"));

        await Assert.ThrowsAsync<BusinessException>(() => service.StartAsync(courtId, open.Id));
    }

    [Fact]
    public async Task CompleteAsync_fixes_an_in_progress_window_and_frees_the_court()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);
        var open = await service.CreateAsync(courtId, new CreateMaintenanceRequest("Resurfacing"));
        Assert.True(await service.IsCourtUnderMaintenanceAsync(courtId));

        var fixedDto = await service.CompleteAsync(courtId, open.Id);

        Assert.Equal(MaintenanceStatus.Completed, fixedDto.Status);
        Assert.Equal(Now, fixedDto.EndUtc);
        Assert.False(await service.IsCourtUnderMaintenanceAsync(courtId)); // available again
    }

    [Fact]
    public async Task CompleteAsync_on_a_scheduled_window_throws_Business()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);
        var scheduled = await service.CreateAsync(
            courtId, new CreateMaintenanceRequest("Planned", StartUtc: Now.AddDays(1)));

        await Assert.ThrowsAsync<BusinessException>(() => service.CompleteAsync(courtId, scheduled.Id));
    }

    [Fact]
    public async Task CancelAsync_cancels_an_open_window()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);
        var open = await service.CreateAsync(courtId, new CreateMaintenanceRequest("Resurfacing"));

        var cancelled = await service.CancelAsync(courtId, open.Id);

        Assert.Equal(MaintenanceStatus.Cancelled, cancelled.Status);
        Assert.False(await service.IsCourtUnderMaintenanceAsync(courtId));
    }

    [Fact]
    public async Task CancelAsync_on_a_scheduled_window_preserves_its_planned_end()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);
        var plannedEnd = Now.AddDays(3).AddHours(2);
        var scheduled = await service.CreateAsync(
            courtId, new CreateMaintenanceRequest("Planned", StartUtc: Now.AddDays(3), EndUtc: plannedEnd));

        var cancelled = await service.CancelAsync(courtId, scheduled.Id);

        Assert.Equal(MaintenanceStatus.Cancelled, cancelled.Status);
        // A not-yet-started window keeps its planned end (only an in-progress cancel ends "now").
        Assert.Equal(plannedEnd, cancelled.EndUtc);
    }

    [Fact]
    public async Task StartAsync_missing_window_throws_NotFound()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);

        await Assert.ThrowsAsync<NotFoundException>(() => service.StartAsync(courtId, 999));
    }

    // --- Read-side predicates -------------------------------------------------------------------

    [Fact]
    public async Task GetCourtIdsUnderMaintenanceAsync_returns_courts_with_an_open_window_overlapping_the_range()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);
        // An open window for next week.
        await service.CreateAsync(
            courtId, new CreateMaintenanceRequest("Next week", StartUtc: Now.AddDays(7), EndUtc: Now.AddDays(8)));

        var overlapping = await service.GetCourtIdsUnderMaintenanceAsync(Now.AddDays(7).AddHours(2), Now.AddDays(7).AddHours(6));
        var before = await service.GetCourtIdsUnderMaintenanceAsync(Now, Now.AddDays(1));

        Assert.Contains(courtId, overlapping);
        Assert.DoesNotContain(courtId, before); // the range ends before the window starts
    }

    [Fact]
    public async Task GetCourtIdsUnderMaintenanceAsync_open_ended_window_covers_any_later_range()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);
        await service.CreateAsync(courtId, new CreateMaintenanceRequest("Open-ended, now")); // InProgress, no end

        var farFuture = await service.GetCourtIdsUnderMaintenanceAsync(Now.AddYears(1), Now.AddYears(1).AddDays(1));

        Assert.Contains(courtId, farFuture);
    }

    [Fact]
    public async Task GetCourtIdsUnderMaintenanceAsync_dedupes_a_court_with_multiple_open_windows()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);
        // Two non-overlapping open windows on the same court, both within the query range.
        await service.CreateAsync(
            courtId, new CreateMaintenanceRequest("W1", StartUtc: Now.AddDays(1), EndUtc: Now.AddDays(1).AddHours(2)));
        await service.CreateAsync(
            courtId, new CreateMaintenanceRequest("W2", StartUtc: Now.AddDays(2), EndUtc: Now.AddDays(2).AddHours(2)));

        var ids = await service.GetCourtIdsUnderMaintenanceAsync(Now, Now.AddDays(5));

        Assert.Single(ids); // distinct — the court id appears once despite two windows
        Assert.Equal(courtId, ids[0]);
    }

    [Fact]
    public async Task GetCourtIdsUnderMaintenanceAsync_excludes_a_window_ending_exactly_at_range_start()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);
        var end = Now.AddDays(2);
        await service.CreateAsync(
            courtId, new CreateMaintenanceRequest("Ends at range start", StartUtc: Now.AddDays(1), EndUtc: end));

        // Half-open ranges: a window ending exactly when the query range begins does NOT overlap (EndUtc > fromUtc).
        var ids = await service.GetCourtIdsUnderMaintenanceAsync(end, end.AddHours(2));

        Assert.Empty(ids);
    }

    // --- History --------------------------------------------------------------------------------

    [Fact]
    public async Task GetHistoryAsync_returns_windows_newest_first()
    {
        await using var db = NewDb();
        var courtId = await SeedCourtAsync(db);
        var service = NewService(db);
        // Two non-overlapping bounded windows (an open-ended one would overlap any later window).
        await service.CreateAsync(
            courtId, new CreateMaintenanceRequest("First", StartUtc: Now.AddDays(1), EndUtc: Now.AddDays(1).AddHours(2)));
        await service.CreateAsync(
            courtId, new CreateMaintenanceRequest("Second", StartUtc: Now.AddDays(5), EndUtc: Now.AddDays(5).AddHours(2)));

        var page = await service.GetHistoryAsync(courtId, new PaginationQuery());

        Assert.Equal(2, page.TotalCount);
        Assert.Equal("Second", page.Items[0].Reason); // highest id (newest) first
    }

    [Fact]
    public async Task GetHistoryAsync_missing_court_throws_NotFound()
    {
        await using var db = NewDb();
        var service = NewService(db);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetHistoryAsync(999, new PaginationQuery()));
    }
}
