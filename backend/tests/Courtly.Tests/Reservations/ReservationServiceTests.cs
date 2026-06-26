using Courtly.Application.Abstractions;
using Courtly.Application.Common.Exceptions;
using Courtly.Application.Courts.Maintenance;
using Courtly.Application.Reservations;
using Courtly.Contracts.Common;
using Courtly.Contracts.Court;
using Courtly.Contracts.Messaging;
using Courtly.Contracts.Reservations;
using Courtly.Domain.Constants;
using Courtly.Domain.Entities;
using Courtly.Domain.Enums;
using Courtly.Infrastructure.Configuration;
using Courtly.Infrastructure.Messaging;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Courtly.Tests.Reservations;

/// <summary>
/// Feature 14 DoD (auto): the reservation engine creates a Pending booking (server-owned price + hold, full
/// preconditions), drives it through the state machine writing an audit row + publishing an event on every transition,
/// rejects overlaps/terminal moves/illegal completions, and enforces ownership on cancel/read. Mirrors the F12/F13
/// service harness — EF InMemory, fresh DB per test, NullLogger, fixed <see cref="IClock"/>, the real
/// <see cref="MaintenanceService"/> wired in so the maintenance→not-bookable path is exercised end-to-end, and a fake
/// event publisher capturing what would hit the bus.
/// </summary>
public class ReservationServiceTests
{
    private const decimal SlotPrice = 30m;
    private const int HoldMinutes = 15;
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
        public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
        public bool IsInRole(string role) => Roles.Contains(role);
    }

    private sealed class FakeEventPublisher : IReservationEventPublisher
    {
        public List<ReservationEvent> Published { get; } = new();

        public Task PublishAsync(ReservationEvent reservationEvent, CancellationToken ct = default)
        {
            Published.Add(reservationEvent);
            return Task.CompletedTask;
        }
    }

    private static CourtlyDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CourtlyDbContext>()
            .UseInMemoryDatabase($"reservations-{Guid.NewGuid()}")
            .Options);

    private static (ReservationService svc, FakeEventPublisher events) NewService(
        CourtlyDbContext db, Guid? userId = null, string[]? roles = null, DateTime? now = null)
    {
        var clock = new TestClock { UtcNow = now ?? Now };
        var current = new TestCurrentUser { UserId = userId, Roles = roles ?? Array.Empty<string>() };
        var maintenance = new MaintenanceService(db, clock, current, NullLogger<MaintenanceService>.Instance);
        var events = new FakeEventPublisher();
        var options = Options.Create(new ReservationOptions { HoldMinutes = HoldMinutes });
        var svc = new ReservationService(
            db, clock, current, maintenance, events, options, NullLogger<ReservationService>.Instance);
        return (svc, events);
    }

    private static MaintenanceService NewMaintenance(CourtlyDbContext db) =>
        new(db, new TestClock(), new TestCurrentUser(), NullLogger<MaintenanceService>.Instance);

    /// <summary>Seeds the FK chain + one active court with one slot, returning their ids. The slot defaults to two
    /// hours from "now" (i.e. bookable); pass <paramref name="slotStart"/> in the past for completion tests.</summary>
    private static async Task<(long courtId, long slotId)> SeedCourtWithSlotAsync(
        CourtlyDbContext db, DateTime? slotStart = null, decimal price = SlotPrice, int durationHours = 1,
        bool courtActive = true, bool slotActive = true)
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
            IsActive = courtActive,
            HourlyPrice = price,
        };
        db.Courts.Add(court);
        await db.SaveChangesAsync();
        var start = slotStart ?? Now.AddHours(2);
        var slot = new TimeSlot
        {
            CourtId = court.Id,
            StartUtc = start,
            EndUtc = start.AddHours(durationHours),
            Price = price,
            Bucket = TimeOfDayBucket.Afternoon,
            IsActive = slotActive,
        };
        db.TimeSlots.Add(slot);
        await db.SaveChangesAsync();
        return (court.Id, slot.Id);
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

    /// <summary>Inserts a reservation directly (bypassing the create preconditions) for transition tests that need a
    /// reservation already in a given state — optionally with a succeeded payment.</summary>
    private static async Task<long> InsertReservationAsync(
        CourtlyDbContext db, Guid userId, long courtId, long slotId, ReservationStatus status,
        decimal price = SlotPrice, bool paid = false)
    {
        var reservation = new Reservation
        {
            UserId = userId,
            CourtId = courtId,
            TimeSlotId = slotId,
            Status = status,
            TotalPrice = price,
            CreatedAtUtc = Now,
        };
        reservation.Audits.Add(new ReservationAudit
        {
            OldStatus = null,
            NewStatus = ReservationStatus.Pending,
            ChangedByUserId = userId,
            CreatedAtUtc = Now,
        });
        if (paid)
        {
            reservation.Payment = new Payment
            {
                Status = PaymentStatus.Succeeded,
                Amount = price,
                AmountChargedCents = (long)(price * 100m),
                ProviderPaymentIntentId = "pi_test",
                IdempotencyKey = Guid.NewGuid().ToString("N"),
                CreatedAtUtc = Now,
                PaidAtUtc = Now,
            };
        }

        db.Reservations.Add(reservation);
        await db.SaveChangesAsync();
        return reservation.Id;
    }

    // --- Create ---------------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_creates_pending_with_server_price_hold_and_audit()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var userId = await SeedUserAsync(db, "alice");
        var (svc, events) = NewService(db, userId);

        var detail = await svc.CreateAsync(new CreateReservationRequest(slotId));

        Assert.Equal(ReservationStatus.Pending, detail.Reservation.Status);
        Assert.Equal("Pending", detail.Reservation.StatusName);
        Assert.Equal(courtId, detail.Reservation.CourtId);
        Assert.Equal(userId, detail.Reservation.UserId);
        Assert.Equal(SlotPrice, detail.Reservation.TotalPrice);        // server-owned price
        Assert.False(detail.Reservation.IsPaid);
        Assert.Equal(Now.AddMinutes(HoldMinutes), detail.Reservation.HoldExpiresAtUtc); // unpaid hold stamped
        Assert.Single(detail.Audits);
        Assert.Null(detail.Audits[0].OldStatus);
        Assert.Equal(ReservationStatus.Pending, detail.Audits[0].NewStatus);
        Assert.Null(detail.Payment);

        var evt = Assert.Single(events.Published);
        Assert.Equal(ReservationRoutingKeys.Created, evt.RoutingKey);
        Assert.Equal(detail.Reservation.Id, evt.ReservationId);
    }

    [Fact]
    public async Task CreateAsync_uses_server_slot_price_for_a_long_duration_booking()
    {
        // A 25-hour slot priced by the catalog (feature 13: rate × duration) → the reservation total is that price,
        // never recomputed/trusted from the client (rubric §7 edge-case pricing, §7.1 server owns the amount).
        await using var db = NewDb();
        var (_, slotId) = await SeedCourtWithSlotAsync(db, price: 500m, durationHours: 25);
        var userId = await SeedUserAsync(db, "long");
        var (svc, _) = NewService(db, userId);

        var detail = await svc.CreateAsync(new CreateReservationRequest(slotId));

        Assert.Equal(500m, detail.Reservation.TotalPrice);
    }

    [Fact]
    public async Task CreateAsync_blocks_when_slot_has_an_active_reservation()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var holder = await SeedUserAsync(db, "holder");
        await InsertReservationAsync(db, holder, courtId, slotId, ReservationStatus.Confirmed);
        var userId = await SeedUserAsync(db, "second");
        var (svc, _) = NewService(db, userId);

        await Assert.ThrowsAsync<ConflictException>(() => svc.CreateAsync(new CreateReservationRequest(slotId)));
    }

    [Fact]
    public async Task CreateAsync_blocks_a_past_slot()
    {
        await using var db = NewDb();
        var (_, slotId) = await SeedCourtWithSlotAsync(db, slotStart: Now.AddHours(-1));
        var userId = await SeedUserAsync(db, "late");
        var (svc, _) = NewService(db, userId);

        await Assert.ThrowsAsync<BusinessException>(() => svc.CreateAsync(new CreateReservationRequest(slotId)));
    }

    [Fact]
    public async Task CreateAsync_blocks_an_inactive_slot()
    {
        await using var db = NewDb();
        var (_, slotId) = await SeedCourtWithSlotAsync(db, slotActive: false);
        var userId = await SeedUserAsync(db, "inactiveslot");
        var (svc, _) = NewService(db, userId);

        await Assert.ThrowsAsync<BusinessException>(() => svc.CreateAsync(new CreateReservationRequest(slotId)));
    }

    [Fact]
    public async Task CreateAsync_blocks_an_inactive_court()
    {
        await using var db = NewDb();
        var (_, slotId) = await SeedCourtWithSlotAsync(db, courtActive: false);
        var userId = await SeedUserAsync(db, "inactivecourt");
        var (svc, _) = NewService(db, userId);

        await Assert.ThrowsAsync<BusinessException>(() => svc.CreateAsync(new CreateReservationRequest(slotId)));
    }

    [Fact]
    public async Task CreateAsync_blocks_a_court_under_maintenance()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var userId = await SeedUserAsync(db, "blocked");
        // Open-ended maintenance window starting now → covers the (future) slot.
        await NewMaintenance(db).CreateAsync(courtId, new CreateMaintenanceRequest("Resurfacing"));
        var (svc, _) = NewService(db, userId);

        await Assert.ThrowsAsync<BusinessException>(() => svc.CreateAsync(new CreateReservationRequest(slotId)));
    }

    [Fact]
    public async Task CreateAsync_missing_slot_throws_NotFound()
    {
        await using var db = NewDb();
        var userId = await SeedUserAsync(db, "ghost");
        var (svc, _) = NewService(db, userId);

        await Assert.ThrowsAsync<NotFoundException>(() => svc.CreateAsync(new CreateReservationRequest(999)));
    }

    [Fact]
    public async Task CreateAsync_unauthenticated_throws_Unauthorized()
    {
        await using var db = NewDb();
        var (_, slotId) = await SeedCourtWithSlotAsync(db);
        var (svc, _) = NewService(db, userId: null);

        await Assert.ThrowsAsync<UnauthorizedException>(() => svc.CreateAsync(new CreateReservationRequest(slotId)));
    }

    // --- Confirm --------------------------------------------------------------------------------

    [Fact]
    public async Task ConfirmAsync_moves_pending_to_confirmed_with_audit_and_event()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var userId = await SeedUserAsync(db, "carol");
        var rid = await InsertReservationAsync(db, userId, courtId, slotId, ReservationStatus.Pending);
        var (svc, events) = NewService(db, userId, roles: new[] { Roles.Admin });

        var detail = await svc.ConfirmAsync(rid);

        Assert.Equal(ReservationStatus.Confirmed, detail.Reservation.Status);
        Assert.Contains(detail.Audits, a => a.NewStatus == ReservationStatus.Confirmed);
        Assert.Contains(events.Published, e => e.RoutingKey == ReservationRoutingKeys.Confirmed);
    }

    [Fact]
    public async Task ConfirmAsync_rejects_a_terminal_reservation()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var userId = await SeedUserAsync(db, "dan");
        var rid = await InsertReservationAsync(db, userId, courtId, slotId, ReservationStatus.Cancelled);
        var (svc, _) = NewService(db, userId, roles: new[] { Roles.Admin });

        await Assert.ThrowsAsync<BusinessException>(() => svc.ConfirmAsync(rid));
    }

    [Fact]
    public async Task ConfirmAsync_missing_reservation_throws_NotFound()
    {
        await using var db = NewDb();
        var (svc, _) = NewService(db, await SeedUserAsync(db, "nf"), roles: new[] { Roles.Admin });

        await Assert.ThrowsAsync<NotFoundException>(() => svc.ConfirmAsync(999));
    }

    // --- Cancel ---------------------------------------------------------------------------------

    [Fact]
    public async Task CancelAsync_by_owner_cancels_with_reason_and_frees_the_slot()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var owner = await SeedUserAsync(db, "owner");
        var (ownerSvc, events) = NewService(db, owner);
        var created = await ownerSvc.CreateAsync(new CreateReservationRequest(slotId));

        var detail = await ownerSvc.CancelAsync(created.Reservation.Id, new CancelReservationRequest("Changed plans"));

        Assert.Equal(ReservationStatus.Cancelled, detail.Reservation.Status);
        Assert.Equal(Now, detail.Reservation.CancelledAtUtc);
        Assert.Equal("Changed plans", detail.Reservation.CancellationReason);
        Assert.Contains(detail.Audits, a => a.NewStatus == ReservationStatus.Cancelled && a.Reason == "Changed plans");
        Assert.Contains(events.Published, e => e.RoutingKey == ReservationRoutingKeys.Cancelled && e.Reason == "Changed plans");

        // Slot freed: a different user can now book it.
        var other = await SeedUserAsync(db, "next");
        var (otherSvc, _) = NewService(db, other);
        var rebooked = await otherSvc.CreateAsync(new CreateReservationRequest(slotId));
        Assert.Equal(ReservationStatus.Pending, rebooked.Reservation.Status);
    }

    [Fact]
    public async Task CancelAsync_by_a_stranger_is_forbidden()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var owner = await SeedUserAsync(db, "owner2");
        var rid = await InsertReservationAsync(db, owner, courtId, slotId, ReservationStatus.Pending);
        var stranger = await SeedUserAsync(db, "stranger");
        var (svc, _) = NewService(db, stranger);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => svc.CancelAsync(rid, new CancelReservationRequest("not mine")));
    }

    [Fact]
    public async Task CancelAsync_by_staff_can_cancel_another_users_booking()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var owner = await SeedUserAsync(db, "owner3");
        var rid = await InsertReservationAsync(db, owner, courtId, slotId, ReservationStatus.Pending);
        var staff = await SeedUserAsync(db, "staff");
        var (svc, _) = NewService(db, staff, roles: new[] { Roles.Staff });

        var detail = await svc.CancelAsync(rid, new CancelReservationRequest("Court closed"));

        Assert.Equal(ReservationStatus.Cancelled, detail.Reservation.Status);
    }

    [Fact]
    public async Task CancelAsync_blocks_a_paid_reservation_without_a_refund()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var owner = await SeedUserAsync(db, "payer");
        var rid = await InsertReservationAsync(db, owner, courtId, slotId, ReservationStatus.Confirmed, paid: true);
        var (svc, _) = NewService(db, owner);

        await Assert.ThrowsAsync<BusinessException>(
            () => svc.CancelAsync(rid, new CancelReservationRequest("Refund me")));
    }

    [Fact]
    public async Task CancelAsync_rejects_a_completed_reservation()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var owner = await SeedUserAsync(db, "done");
        var rid = await InsertReservationAsync(db, owner, courtId, slotId, ReservationStatus.Completed);
        var (svc, _) = NewService(db, owner);

        await Assert.ThrowsAsync<BusinessException>(
            () => svc.CancelAsync(rid, new CancelReservationRequest("too late")));
    }

    [Fact]
    public async Task CancelAsync_requires_a_reason()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var owner = await SeedUserAsync(db, "noreason");
        var rid = await InsertReservationAsync(db, owner, courtId, slotId, ReservationStatus.Pending);
        var (svc, _) = NewService(db, owner);

        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CancelAsync(rid, new CancelReservationRequest("   ")));
    }

    // --- Complete -------------------------------------------------------------------------------

    [Fact]
    public async Task CompleteAsync_completes_a_confirmed_reservation_after_its_slot_ends()
    {
        await using var db = NewDb();
        // Slot in the past (ended an hour ago) so completion is allowed.
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db, slotStart: Now.AddHours(-2));
        var userId = await SeedUserAsync(db, "finished");
        var rid = await InsertReservationAsync(db, userId, courtId, slotId, ReservationStatus.Confirmed);
        var (svc, events) = NewService(db, userId, roles: new[] { Roles.Admin });

        var detail = await svc.CompleteAsync(rid);

        Assert.Equal(ReservationStatus.Completed, detail.Reservation.Status);
        Assert.Contains(detail.Audits, a => a.NewStatus == ReservationStatus.Completed);
        Assert.Contains(events.Published, e => e.RoutingKey == ReservationRoutingKeys.Completed);
    }

    [Fact]
    public async Task CompleteAsync_blocks_before_the_slot_has_ended()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db); // slot two hours in the future
        var userId = await SeedUserAsync(db, "early");
        var rid = await InsertReservationAsync(db, userId, courtId, slotId, ReservationStatus.Confirmed);
        var (svc, _) = NewService(db, userId, roles: new[] { Roles.Admin });

        await Assert.ThrowsAsync<BusinessException>(() => svc.CompleteAsync(rid));
    }

    [Fact]
    public async Task CompleteAsync_rejects_a_pending_reservation()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db, slotStart: Now.AddHours(-2));
        var userId = await SeedUserAsync(db, "stillpending");
        var rid = await InsertReservationAsync(db, userId, courtId, slotId, ReservationStatus.Pending);
        var (svc, _) = NewService(db, userId, roles: new[] { Roles.Admin });

        await Assert.ThrowsAsync<BusinessException>(() => svc.CompleteAsync(rid));
    }

    // --- Reads ----------------------------------------------------------------------------------

    [Fact]
    public async Task GetByIdAsync_returns_detail_with_audits_and_payment_for_the_owner()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var owner = await SeedUserAsync(db, "reader");
        var rid = await InsertReservationAsync(db, owner, courtId, slotId, ReservationStatus.Confirmed, paid: true);
        var (svc, _) = NewService(db, owner);

        var detail = await svc.GetByIdAsync(rid);

        Assert.Equal(rid, detail.Reservation.Id);
        Assert.True(detail.Reservation.IsPaid);
        Assert.NotNull(detail.Payment);
        Assert.Equal(PaymentStatus.Succeeded, detail.Payment!.Status);
        Assert.NotEmpty(detail.Audits);
        Assert.Equal("Afternoon", detail.Reservation.BucketName);
    }

    [Fact]
    public async Task GetByIdAsync_forbids_a_stranger()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var owner = await SeedUserAsync(db, "owner4");
        var rid = await InsertReservationAsync(db, owner, courtId, slotId, ReservationStatus.Pending);
        var stranger = await SeedUserAsync(db, "peeker");
        var (svc, _) = NewService(db, stranger);

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.GetByIdAsync(rid));
    }

    [Fact]
    public async Task GetByIdAsync_lets_admin_view_any_reservation()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var owner = await SeedUserAsync(db, "owner5");
        var rid = await InsertReservationAsync(db, owner, courtId, slotId, ReservationStatus.Pending);
        var admin = await SeedUserAsync(db, "admin");
        var (svc, _) = NewService(db, admin, roles: new[] { Roles.Admin });

        var detail = await svc.GetByIdAsync(rid);

        Assert.Equal(rid, detail.Reservation.Id);
    }

    [Fact]
    public async Task ListMineAsync_returns_only_the_callers_reservations()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var (_, slot2) = await SeedCourtWithSlotAsync(db, slotStart: Now.AddHours(5));
        var me = await SeedUserAsync(db, "me");
        var someoneElse = await SeedUserAsync(db, "them");
        await InsertReservationAsync(db, me, courtId, slotId, ReservationStatus.Pending);
        await InsertReservationAsync(db, someoneElse, courtId, slot2, ReservationStatus.Pending);
        var (svc, _) = NewService(db, me);

        var page = await svc.ListMineAsync(new ReservationListQuery(), new PaginationQuery());

        Assert.Equal(1, page.TotalCount);
        Assert.All(page.Items, r => Assert.Equal(me, r.UserId));
    }

    [Fact]
    public async Task ListAsync_admin_filters_by_status_and_court()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var (_, slot2) = await SeedCourtWithSlotAsync(db, slotStart: Now.AddHours(6));
        var u1 = await SeedUserAsync(db, "u1");
        var u2 = await SeedUserAsync(db, "u2");
        await InsertReservationAsync(db, u1, courtId, slotId, ReservationStatus.Pending);
        await InsertReservationAsync(db, u2, courtId, slot2, ReservationStatus.Confirmed);
        var (svc, _) = NewService(db, await SeedUserAsync(db, "boss"), roles: new[] { Roles.Admin });

        var pending = await svc.ListAsync(
            new ReservationListQuery(Status: ReservationStatus.Pending, CourtId: courtId), new PaginationQuery());

        Assert.Equal(1, pending.TotalCount);
        Assert.Equal(ReservationStatus.Pending, pending.Items[0].Status);
    }
}
