using Courtly.Application.Common.Exceptions;
using Courtly.Application.Payments;
using Courtly.Contracts.Messaging;
using Courtly.Contracts.Payments;
using Courtly.Domain.Constants;
using Courtly.Domain.Entities;
using Courtly.Domain.Enums;
using Courtly.Infrastructure.Configuration;
using Courtly.Infrastructure.Payments;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Courtly.Tests.Payments;

/// <summary>
/// Feature 16 DoD (auto): the payments engine creates a PaymentIntent for a Pending, owned reservation (server-owned
/// amount + idempotency key), finalizes the charge idempotently from the webhook (Succeeded + actually-charged cents,
/// Pending → Confirmed, events), guards against double payment, and refunds on the actually-charged amount while
/// cancelling the reservation. Mirrors the F14 service harness — EF InMemory, fresh DB per test, NullLogger, fixed
/// clock, and fakes for the Stripe gateway + both event publishers.
/// </summary>
public class PaymentServiceTests
{
    private const decimal SlotPrice = 30m;
    private const string IntentId = "pi_test_123";
    private static readonly DateTime Now = new(2026, 6, 25, 12, 0, 0, DateTimeKind.Utc);

    private static CourtlyDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CourtlyDbContext>()
            .UseInMemoryDatabase($"payments-{Guid.NewGuid()}")
            .Options);

    private static (PaymentService svc, FakeStripeGateway stripe, FakePaymentEventPublisher payEvents,
        FakeReservationEventPublisher resEvents) NewService(
            CourtlyDbContext db, Guid? userId = null, string[]? roles = null)
    {
        var clock = new TestClock { UtcNow = Now };
        var current = new TestCurrentUser { UserId = userId, Roles = roles ?? Array.Empty<string>() };
        var stripe = new FakeStripeGateway();
        var payEvents = new FakePaymentEventPublisher();
        var resEvents = new FakeReservationEventPublisher();
        var options = Options.Create(new StripeOptions
        {
            SecretKey = "sk_test",
            PublishableKey = "pk_test_x",
            WebhookSecret = "whsec_test",
            Currency = "usd",
        });
        var svc = new PaymentService(
            db, clock, current, stripe, resEvents, payEvents, options, NullLogger<PaymentService>.Instance);
        return (svc, stripe, payEvents, resEvents);
    }

    private static async Task<(long courtId, long slotId)> SeedCourtWithSlotAsync(
        CourtlyDbContext db, decimal price = SlotPrice)
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
        var start = Now.AddHours(2);
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

    private static async Task<long> InsertReservationAsync(
        CourtlyDbContext db, Guid userId, long courtId, long slotId, ReservationStatus status,
        decimal totalPrice = SlotPrice)
    {
        var reservation = new Reservation
        {
            UserId = userId,
            CourtId = courtId,
            TimeSlotId = slotId,
            Status = status,
            TotalPrice = totalPrice,
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

    private static async Task InsertPaymentAsync(
        CourtlyDbContext db, long reservationId, PaymentStatus status, decimal amount,
        long? chargedCents = null, string intentId = IntentId)
    {
        db.Payments.Add(new Payment
        {
            ReservationId = reservationId,
            Status = status,
            Amount = amount,
            AmountChargedCents = chargedCents,
            ProviderPaymentIntentId = intentId,
            IdempotencyKey = $"intent-resv-{reservationId}",
            CreatedAtUtc = Now,
            PaidAtUtc = status == PaymentStatus.Succeeded ? Now : null,
        });
        await db.SaveChangesAsync();
    }

    // --- Create intent --------------------------------------------------------------------------

    [Fact]
    public async Task CreateIntentAsync_creates_pending_payment_with_server_amount_and_idempotency_key()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var userId = await SeedUserAsync(db, "alice");
        var rid = await InsertReservationAsync(db, userId, courtId, slotId, ReservationStatus.Pending);
        var (svc, stripe, _, _) = NewService(db, userId);

        var response = await svc.CreateIntentAsync(new CreatePaymentIntentRequest(rid));

        Assert.Equal(3000, response.AmountCents);                 // 30.00 → cents, from the server-owned price
        Assert.Equal("usd", response.Currency);
        Assert.Equal("pk_test_x", response.PublishableKey);
        Assert.Equal(stripe.NextClientSecret, response.ClientSecret);

        var created = Assert.Single(stripe.CreatedIntents);
        Assert.Equal(3000, created.AmountCents);
        Assert.Equal($"intent-resv-{rid}", created.IdempotencyKey);

        var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.ReservationId == rid);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(IntentId, payment.ProviderPaymentIntentId);
        Assert.Equal(30m, payment.Amount);
    }

    [Fact]
    public async Task CreateIntentAsync_by_a_stranger_is_forbidden()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var owner = await SeedUserAsync(db, "owner");
        var rid = await InsertReservationAsync(db, owner, courtId, slotId, ReservationStatus.Pending);
        var stranger = await SeedUserAsync(db, "stranger");
        var (svc, _, _, _) = NewService(db, stranger);

        await Assert.ThrowsAsync<ForbiddenException>(() => svc.CreateIntentAsync(new CreatePaymentIntentRequest(rid)));
    }

    [Fact]
    public async Task CreateIntentAsync_rejects_a_non_pending_reservation()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var userId = await SeedUserAsync(db, "confirmed");
        var rid = await InsertReservationAsync(db, userId, courtId, slotId, ReservationStatus.Confirmed);
        var (svc, _, _, _) = NewService(db, userId);

        await Assert.ThrowsAsync<BusinessException>(() => svc.CreateIntentAsync(new CreatePaymentIntentRequest(rid)));
    }

    [Fact]
    public async Task CreateIntentAsync_rejects_an_already_paid_reservation()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var userId = await SeedUserAsync(db, "paid");
        var rid = await InsertReservationAsync(db, userId, courtId, slotId, ReservationStatus.Pending);
        await InsertPaymentAsync(db, rid, PaymentStatus.Succeeded, 30m, chargedCents: 3000);
        var (svc, _, _, _) = NewService(db, userId);

        await Assert.ThrowsAsync<BusinessException>(() => svc.CreateIntentAsync(new CreatePaymentIntentRequest(rid)));
    }

    [Fact]
    public async Task CreateIntentAsync_retry_returns_the_same_intent_without_a_second_charge()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var userId = await SeedUserAsync(db, "retry");
        var rid = await InsertReservationAsync(db, userId, courtId, slotId, ReservationStatus.Pending);
        var (svc, stripe, _, _) = NewService(db, userId);

        var first = await svc.CreateIntentAsync(new CreatePaymentIntentRequest(rid));
        var second = await svc.CreateIntentAsync(new CreatePaymentIntentRequest(rid));

        // Exactly one Stripe intent created — the retry re-fetched the same one (no double charge / double-pay guard).
        Assert.Single(stripe.CreatedIntents);
        Assert.Equal(first.ClientSecret, second.ClientSecret);
        Assert.Equal(1, await db.Payments.CountAsync(p => p.ReservationId == rid));
    }

    // --- Webhook finalize -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessWebhookAsync_finalizes_payment_and_confirms_reservation()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var userId = await SeedUserAsync(db, "payer");
        var rid = await InsertReservationAsync(db, userId, courtId, slotId, ReservationStatus.Pending);
        await InsertPaymentAsync(db, rid, PaymentStatus.Pending, 30m);
        var (svc, stripe, payEvents, resEvents) = NewService(db, userId);
        stripe.NextWebhookEvent = new StripeWebhookEvent(StripeEventTypes.PaymentIntentSucceeded, IntentId, 3000);

        await svc.ProcessWebhookAsync("{}", "sig");

        var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.ReservationId == rid);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(3000, payment.AmountChargedCents);          // the actually-charged amount from the webhook
        Assert.Equal(Now, payment.PaidAtUtc);

        var reservation = await db.Reservations.AsNoTracking().SingleAsync(r => r.Id == rid);
        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        Assert.Contains(
            await db.ReservationAudits.AsNoTracking().Where(a => a.ReservationId == rid).ToListAsync(),
            a => a.NewStatus == ReservationStatus.Confirmed);

        Assert.Contains(payEvents.Published, e => e.RoutingKey == PaymentRoutingKeys.Succeeded);
        Assert.Contains(resEvents.Published, e => e.RoutingKey == ReservationRoutingKeys.Confirmed);
    }

    [Fact]
    public async Task ProcessWebhookAsync_replayed_event_is_idempotent()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var userId = await SeedUserAsync(db, "replay");
        var rid = await InsertReservationAsync(db, userId, courtId, slotId, ReservationStatus.Pending);
        await InsertPaymentAsync(db, rid, PaymentStatus.Pending, 30m);
        var (svc, stripe, payEvents, resEvents) = NewService(db, userId);
        stripe.NextWebhookEvent = new StripeWebhookEvent(StripeEventTypes.PaymentIntentSucceeded, IntentId, 3000);

        await svc.ProcessWebhookAsync("{}", "sig");
        await svc.ProcessWebhookAsync("{}", "sig");  // replay — must be a no-op

        Assert.Equal(PaymentStatus.Succeeded, (await db.Payments.AsNoTracking().SingleAsync(p => p.ReservationId == rid)).Status);
        // No double effect: one confirm audit, one of each event.
        Assert.Equal(1, await db.ReservationAudits.CountAsync(a => a.ReservationId == rid && a.NewStatus == ReservationStatus.Confirmed));
        Assert.Single(payEvents.Published);
        Assert.Single(resEvents.Published);
    }

    [Fact]
    public async Task ProcessWebhookAsync_invalid_signature_throws_validation()
    {
        await using var db = NewDb();
        var (svc, stripe, _, _) = NewService(db);
        stripe.ThrowOnConstruct = true;

        await Assert.ThrowsAsync<ValidationException>(() => svc.ProcessWebhookAsync("{}", "bad-sig"));
    }

    [Fact]
    public async Task ProcessWebhookAsync_unknown_intent_is_ignored()
    {
        await using var db = NewDb();
        var (svc, stripe, payEvents, resEvents) = NewService(db);
        stripe.NextWebhookEvent = new StripeWebhookEvent(StripeEventTypes.PaymentIntentSucceeded, "pi_unknown", 1000);

        await svc.ProcessWebhookAsync("{}", "sig");   // no matching payment → ignored, no throw

        Assert.Empty(payEvents.Published);
        Assert.Empty(resEvents.Published);
    }

    [Fact]
    public async Task ProcessWebhookAsync_ignores_unhandled_event_types()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var userId = await SeedUserAsync(db, "other");
        var rid = await InsertReservationAsync(db, userId, courtId, slotId, ReservationStatus.Pending);
        await InsertPaymentAsync(db, rid, PaymentStatus.Pending, 30m);
        var (svc, stripe, payEvents, _) = NewService(db, userId);
        stripe.NextWebhookEvent = new StripeWebhookEvent("payment_intent.payment_failed", IntentId, null);

        await svc.ProcessWebhookAsync("{}", "sig");

        Assert.Equal(PaymentStatus.Pending, (await db.Payments.AsNoTracking().SingleAsync(p => p.ReservationId == rid)).Status);
        Assert.Empty(payEvents.Published);
    }

    // --- Refund ---------------------------------------------------------------------------------

    [Fact]
    public async Task RefundAsync_refunds_the_charged_amount_and_cancels_the_reservation()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var owner = await SeedUserAsync(db, "customer");
        var rid = await InsertReservationAsync(db, owner, courtId, slotId, ReservationStatus.Confirmed);
        // Charged amount (2500) deliberately differs from the catalog price (3000) to prove the refund uses the
        // actually-charged cents, never a recomputed price (rubric §7.1).
        await InsertPaymentAsync(db, rid, PaymentStatus.Succeeded, 30m, chargedCents: 2500);
        var admin = await SeedUserAsync(db, "admin");
        var (svc, stripe, payEvents, resEvents) = NewService(db, admin, roles: new[] { Roles.Admin });

        var dto = await svc.RefundAsync(rid, new RefundReservationRequest("Customer requested"));

        var refundCall = Assert.Single(stripe.CreatedRefunds);
        Assert.Equal(2500, refundCall.AmountCents);              // charged amount, not 3000
        Assert.Equal(IntentId, refundCall.IntentId);

        Assert.Equal(PaymentStatus.Refunded, dto.Status);
        Assert.NotNull(dto.Refund);
        Assert.Equal(25m, dto.Refund!.Amount);

        var reservation = await db.Reservations.AsNoTracking().SingleAsync(r => r.Id == rid);
        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
        Assert.Equal("Customer requested", reservation.CancellationReason);
        Assert.Equal(Now, reservation.CancelledAtUtc);

        Assert.Contains(payEvents.Published, e => e.RoutingKey == PaymentRoutingKeys.Refunded);
        Assert.Contains(resEvents.Published, e => e.RoutingKey == ReservationRoutingKeys.Cancelled);
    }

    [Fact]
    public async Task RefundAsync_rejects_a_second_refund()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var owner = await SeedUserAsync(db, "refunded");
        var rid = await InsertReservationAsync(db, owner, courtId, slotId, ReservationStatus.Cancelled);
        await InsertPaymentAsync(db, rid, PaymentStatus.Refunded, 30m, chargedCents: 3000);
        var (svc, _, _, _) = NewService(db, await SeedUserAsync(db, "admin2"), roles: new[] { Roles.Admin });

        await Assert.ThrowsAsync<BusinessException>(
            () => svc.RefundAsync(rid, new RefundReservationRequest("again")));
    }

    [Fact]
    public async Task RefundAsync_rejects_an_unpaid_reservation()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var owner = await SeedUserAsync(db, "unpaid");
        var rid = await InsertReservationAsync(db, owner, courtId, slotId, ReservationStatus.Pending);
        var (svc, _, _, _) = NewService(db, await SeedUserAsync(db, "admin3"), roles: new[] { Roles.Admin });

        await Assert.ThrowsAsync<BusinessException>(
            () => svc.RefundAsync(rid, new RefundReservationRequest("no payment")));
    }

    [Fact]
    public async Task RefundAsync_rejects_a_completed_reservation_without_calling_stripe()
    {
        await using var db = NewDb();
        var (courtId, slotId) = await SeedCourtWithSlotAsync(db);
        var owner = await SeedUserAsync(db, "completed");
        var rid = await InsertReservationAsync(db, owner, courtId, slotId, ReservationStatus.Completed);
        await InsertPaymentAsync(db, rid, PaymentStatus.Succeeded, 30m, chargedCents: 3000);
        var (svc, stripe, _, _) = NewService(db, await SeedUserAsync(db, "admin4"), roles: new[] { Roles.Admin });

        await Assert.ThrowsAsync<BusinessException>(
            () => svc.RefundAsync(rid, new RefundReservationRequest("too late")));

        // The transition is proven illegal BEFORE the external call, so Stripe is never asked to refund.
        Assert.Empty(stripe.CreatedRefunds);
    }
}
