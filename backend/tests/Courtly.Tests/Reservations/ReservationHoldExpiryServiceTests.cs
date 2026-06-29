using Courtly.Application.Abstractions;
using Courtly.Application.Reservations;
using Courtly.Contracts.Messaging;
using Courtly.Domain.Entities;
using Courtly.Domain.Enums;
using Courtly.Infrastructure.Messaging;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Courtly.Tests.Reservations;

/// <summary>
/// Feature 17 (auto): the hold-expiry sweeper auto-cancels Pending reservations whose hold deadline has passed, writing
/// a system-actor audit row and publishing a Cancelled event per row, and leaves everything else untouched. Mirrors the
/// feature 14 service harness — EF InMemory, fresh DB per test, NullLogger, a fixed <see cref="IClock"/>, and a fake
/// event publisher capturing what would hit the bus. No <c>ICurrentUser</c>: the sweeper is a system actor.
/// </summary>
public class ReservationHoldExpiryServiceTests
{
    private const decimal SlotPrice = 30m;
    private static readonly DateTime Now = new(2026, 6, 25, 12, 0, 0, DateTimeKind.Utc);

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow { get; init; } = Now;
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
            .UseInMemoryDatabase($"hold-expiry-{Guid.NewGuid()}")
            .Options);

    private static (ReservationHoldExpiryService svc, FakeEventPublisher events) NewService(
        CourtlyDbContext db, DateTime? now = null)
    {
        var clock = new TestClock { UtcNow = now ?? Now };
        var events = new FakeEventPublisher();
        var svc = new ReservationHoldExpiryService(
            db, events, clock, NullLogger<ReservationHoldExpiryService>.Instance);
        return (svc, events);
    }

    /// <summary>Seeds the FK chain + one active court with one slot, returning their ids. The slot defaults to two
    /// hours from "now" (the expiry sweeper never inspects the slot window for eligibility).</summary>
    private static async Task<(long courtId, long slotId)> SeedCourtWithSlotAsync(
        CourtlyDbContext db, DateTime? slotStart = null, decimal price = SlotPrice)
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
            HourlyPrice = price,
        };
        db.Courts.Add(court);
        await db.SaveChangesAsync();
        var start = slotStart ?? Now.AddHours(2);
        var slot = new TimeSlot
        {
            CourtId = court.Id,
            StartUtc = start,
            EndUtc = start.AddHours(1),
            Price = price,
            Bucket = TimeOfDayBucket.Afternoon,
            IsActive = true,
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

    /// <summary>Inserts a reservation directly (bypassing the create preconditions) in a given state with an explicit
    /// hold deadline, seeding its opening Pending audit row — the same precedent as the feature 14 harness.</summary>
    private static async Task<long> InsertReservationAsync(
        CourtlyDbContext db, Guid userId, long courtId, long slotId, ReservationStatus status,
        DateTime? holdExpiresAtUtc, decimal price = SlotPrice)
    {
        var reservation = new Reservation
        {
            UserId = userId,
            CourtId = courtId,
            TimeSlotId = slotId,
            Status = status,
            TotalPrice = price,
            HoldExpiresAtUtc = holdExpiresAtUtc,
            CreatedAtUtc = Now,
        };
        reservation.Audits.Add(new ReservationAudit
        {
            OldStatus = null,
            NewStatus = ReservationStatus.Pending,
            ChangedByUserId = userId,
            CreatedAtUtc = Now,
        });
        db.Reservations.Add(reservation);
        await db.SaveChangesAsync();
        return reservation.Id;
    }

    [Fact]
    public async Task CancelExpiredHoldsAsync_cancels_an_expired_pending_hold_with_system_audit_and_event()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var userId = await SeedUserAsync(db, "expired");
        // Hold deadline an hour in the past → eligible for the sweep.
        var rid = await InsertReservationAsync(
            db, userId, courtId, slotId, ReservationStatus.Pending, holdExpiresAtUtc: Now.AddHours(-1));
        var (svc, events) = NewService(db);

        var count = await svc.CancelExpiredHoldsAsync(100);

        Assert.Equal(1, count);

        var reservation = await db.Reservations
            .Include(r => r.Audits)
            .SingleAsync(r => r.Id == rid);
        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
        Assert.Equal(Now, reservation.CancelledAtUtc);
        Assert.Equal("Reservation hold expired", reservation.CancellationReason);

        // Exactly one cancellation audit, recorded by the system actor (no user id).
        var audit = Assert.Single(reservation.Audits, a => a.NewStatus == ReservationStatus.Cancelled);
        Assert.Equal(ReservationStatus.Pending, audit.OldStatus);
        Assert.Null(audit.ChangedByUserId);

        // Exactly one Cancelled event carrying the right ids + slot window.
        var evt = Assert.Single(events.Published);
        Assert.Equal(ReservationRoutingKeys.Cancelled, evt.RoutingKey);
        Assert.Equal(rid, evt.ReservationId);
        Assert.Equal(courtId, evt.CourtId);
        Assert.Equal(slotId, evt.TimeSlotId);
        Assert.Equal(userId, evt.UserId);
        Assert.Equal(ReservationStatus.Cancelled, evt.Status);
        Assert.Equal(Now.AddHours(2), evt.SlotStartUtc);
        Assert.Equal(Now.AddHours(3), evt.SlotEndUtc);
        Assert.Equal("Reservation hold expired", evt.Reason);
    }

    [Fact]
    public async Task CancelExpiredHoldsAsync_leaves_confirmed_future_and_holdless_reservations_untouched()
    {
        await using var db = NewDb();
        var (court1, slot1) = await SeedCourtWithSlotAsync(db);
        var (court2, slot2) = await SeedCourtWithSlotAsync(db, slotStart: Now.AddHours(4));
        var (court3, slot3) = await SeedCourtWithSlotAsync(db, slotStart: Now.AddHours(6));
        var userId = await SeedUserAsync(db, "untouched");
        // Confirmed (not Pending) — already paid, an expired hold is irrelevant.
        await InsertReservationAsync(
            db, userId, court1, slot1, ReservationStatus.Confirmed, holdExpiresAtUtc: Now.AddHours(-1));
        // Pending but the hold deadline is still in the future.
        await InsertReservationAsync(
            db, userId, court2, slot2, ReservationStatus.Pending, holdExpiresAtUtc: Now.AddHours(1));
        // Pending with no hold deadline at all (never expires this way).
        await InsertReservationAsync(
            db, userId, court3, slot3, ReservationStatus.Pending, holdExpiresAtUtc: null);
        var (svc, events) = NewService(db);

        var count = await svc.CancelExpiredHoldsAsync(100);

        Assert.Equal(0, count);
        Assert.Empty(events.Published);
        Assert.All(
            await db.Reservations.ToListAsync(),
            r => Assert.NotEqual(ReservationStatus.Cancelled, r.Status));
    }

    [Fact]
    public async Task CancelExpiredHoldsAsync_cancels_only_up_to_the_batch_size_in_one_call()
    {
        await using var db = NewDb();
        var userId = await SeedUserAsync(db, "backlog");
        // Three expired holds, each on its own slot (the active-overlap index allows only one active row per slot).
        for (var i = 0; i < 3; i++)
        {
            var (courtId, slotId) = await SeedCourtWithSlotAsync(db, slotStart: Now.AddHours(2 + i));
            await InsertReservationAsync(
                db, userId, courtId, slotId, ReservationStatus.Pending,
                holdExpiresAtUtc: Now.AddMinutes(-(i + 1)));
        }
        var (svc, events) = NewService(db);

        var count = await svc.CancelExpiredHoldsAsync(2);

        Assert.Equal(2, count);
        Assert.Equal(2, events.Published.Count);
        Assert.Equal(
            2, await db.Reservations.CountAsync(r => r.Status == ReservationStatus.Cancelled));
        Assert.Equal(
            1, await db.Reservations.CountAsync(r => r.Status == ReservationStatus.Pending));
    }
}
