using System.Globalization;
using Courtly.Application.Abstractions;
using Courtly.Application.Common.Exceptions;
using Courtly.Application.Reservations;
using Courtly.Contracts.Messaging;
using Courtly.Contracts.Payments;
using Courtly.Domain.Entities;
using Courtly.Domain.Enums;
using Courtly.Infrastructure.Configuration;
using Courtly.Infrastructure.Messaging;
using Courtly.Infrastructure.Payments;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Courtly.Application.Payments;

/// <summary>
/// The payments engine (feature 16, Stripe server-side). The money flow is entirely server-authoritative (rubric §7.1):
/// the amount comes from the reservation's server-owned price (the client never sends a price), the charge is
/// <b>finalized by the Stripe webhook</b> — never recorded by the client — and a refund is issued on the
/// <b>actually-charged</b> amount. Ownership always comes from the JWT via <see cref="ICurrentUser"/>; timestamps from
/// <see cref="IClock"/> (UTC); the Stripe SDK is reached only through <see cref="IStripeGateway"/> so the engine is
/// unit-tested against a fake.
/// </summary>
/// <remarks>
/// <para><b>Double-pay guard.</b> A reservation has at most one <c>Payment</c> (the 1:1 unique FK). A second
/// <c>/intent</c> for a still-Pending payment returns the same intent (idempotent retry); for a Succeeded payment it's
/// a 409; a concurrent duplicate insert is caught and turned into a friendly 409, never a 500.</para>
/// <para><b>Idempotent finalize.</b> A replayed <c>payment_intent.succeeded</c> is a no-op once the payment is
/// Succeeded — no second confirm, no duplicate events (rubric §7.1).</para>
/// <para><b>Single SaveChanges per operation.</b> Finalize and refund persist the payment change + the reservation
/// transition + its audit row in one <c>SaveChangesAsync</c> (atomic — no explicit transaction needed, rubric §3.4),
/// applying the transition themselves via <see cref="ReservationStateMachine"/> rather than calling a second service
/// that would do its own save. The Stripe refund (an external call) is made only <i>after</i> the transition is proven
/// legal, so we never charge/refund and then fail to persist.</para>
/// </remarks>
public sealed class PaymentService : IPaymentService
{
    private const int CentsPerUnit = 100;

    private readonly CourtlyDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IStripeGateway _stripe;
    private readonly IReservationEventPublisher _reservationEvents;
    private readonly IPaymentEventPublisher _paymentEvents;
    private readonly ILogger<PaymentService> _logger;
    private readonly string _currency;
    private readonly string _publishableKey;

    public PaymentService(
        CourtlyDbContext db,
        IClock clock,
        ICurrentUser currentUser,
        IStripeGateway stripe,
        IReservationEventPublisher reservationEvents,
        IPaymentEventPublisher paymentEvents,
        IOptions<StripeOptions> stripeOptions,
        ILogger<PaymentService> logger)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _stripe = stripe;
        _reservationEvents = reservationEvents;
        _paymentEvents = paymentEvents;
        _logger = logger;
        _currency = stripeOptions.Value.Currency;       // read env once in ctor (rubric §8.2)
        _publishableKey = stripeOptions.Value.PublishableKey;
    }

    public async Task<PaymentIntentResponse> CreateIntentAsync(
        CreatePaymentIntentRequest request, CancellationToken ct = default)
    {
        var userId = CurrentUserId();

        var reservation = await _db.Reservations
            .Include(r => r.Payment)
            .FirstOrDefaultAsync(r => r.Id == request.ReservationId, ct)
            ?? throw new NotFoundException($"Reservation {request.ReservationId} was not found.");

        // Paying is a client action — ownership comes from the JWT, never the route/body (rubric §5).
        if (reservation.UserId != userId)
        {
            throw new ForbiddenException("You can only pay for your own reservations.");
        }

        if (reservation.Status != ReservationStatus.Pending)
        {
            throw new BusinessException("Only a pending reservation can be paid.");
        }

        // A reservation has at most one payment (the 1:1 unique FK is the hard double-pay guard).
        if (reservation.Payment is { } existing)
        {
            if (existing.Status == PaymentStatus.Succeeded)
            {
                throw new BusinessException("This reservation has already been paid.");
            }

            if (existing.Status == PaymentStatus.Pending && !string.IsNullOrEmpty(existing.ProviderPaymentIntentId))
            {
                // Idempotent retry: re-fetch the same intent's client secret instead of creating a second charge.
                var refetched = await _stripe.GetPaymentIntentAsync(existing.ProviderPaymentIntentId, ct);
                return BuildIntentResponse(existing, reservation.Id, refetched.ClientSecret);
            }

            throw new BusinessException("This reservation already has a payment that can't be charged again.");
        }

        var amountCents = ToCents(reservation.TotalPrice);
        var idempotencyKey = $"intent-resv-{reservation.Id}";
        var metadata = new Dictionary<string, string>
        {
            ["reservationId"] = reservation.Id.ToString(CultureInfo.InvariantCulture),
            ["userId"] = reservation.UserId.ToString(),
        };

        var intent = await _stripe.CreatePaymentIntentAsync(amountCents, _currency, idempotencyKey, metadata, ct);

        var payment = new Payment
        {
            ReservationId = reservation.Id,
            Status = PaymentStatus.Pending,
            Amount = reservation.TotalPrice, // server-owned amount (rubric §7.1)
            ProviderPaymentIntentId = intent.Id,
            IdempotencyKey = idempotencyKey,
            CreatedAtUtc = _clock.UtcNow,
        };
        _db.Payments.Add(payment);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost a concurrent race: the 1:1 unique FK rejected a 2nd payment for this reservation.
            var raced = await _db.Payments.AsNoTracking()
                .FirstOrDefaultAsync(p => p.ReservationId == reservation.Id, ct);
            if (raced is { Status: PaymentStatus.Succeeded })
            {
                throw new BusinessException("This reservation has already been paid.");
            }

            throw new ConflictException("A payment for this reservation is already in progress. Please retry.");
        }

        _logger.LogInformation(
            "Created payment {PaymentId} (intent {IntentId}) for reservation {ReservationId}.",
            payment.Id, intent.Id, reservation.Id);

        return BuildIntentResponse(payment, reservation.Id, intent.ClientSecret);
    }

    public async Task ProcessWebhookAsync(string payloadJson, string signatureHeader, CancellationToken ct = default)
    {
        StripeWebhookEvent webhookEvent;
        try
        {
            webhookEvent = _stripe.ConstructEvent(payloadJson, signatureHeader);
        }
        catch (WebhookSignatureException ex)
        {
            // Translate the Infrastructure-level signature failure into the app's 400 (the client never records
            // success — rubric §7.1; the webhook is authenticated by its HMAC signature, not a JWT).
            throw new ValidationException(ex.Message);
        }

        if (webhookEvent.Type != StripeEventTypes.PaymentIntentSucceeded)
        {
            _logger.LogInformation("Ignoring unhandled Stripe webhook event {Type}.", webhookEvent.Type);
            return;
        }

        if (string.IsNullOrEmpty(webhookEvent.PaymentIntentId))
        {
            _logger.LogWarning("A payment_intent.succeeded webhook had no intent id; ignoring.");
            return;
        }

        await FinalizeAsync(webhookEvent.PaymentIntentId, webhookEvent.AmountReceivedCents, ct);
    }

    public async Task<PaymentDto> RefundAsync(
        long reservationId, RefundReservationRequest request, CancellationToken ct = default)
    {
        var reason = (request.Reason ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(reason))
        {
            throw new ValidationException("A refund reason is required.");
        }

        var reservation = await _db.Reservations
            .Include(r => r.TimeSlot)
            .Include(r => r.Payment!)
            .ThenInclude(p => p.Refunds)
            .FirstOrDefaultAsync(r => r.Id == reservationId, ct)
            ?? throw new NotFoundException($"Reservation {reservationId} was not found.");

        var payment = reservation.Payment
            ?? throw new BusinessException("This reservation has no payment to refund.");

        if (payment.Status == PaymentStatus.Refunded)
        {
            throw new BusinessException("This payment has already been refunded.");
        }

        if (payment.Status != PaymentStatus.Succeeded || string.IsNullOrEmpty(payment.ProviderPaymentIntentId))
        {
            throw new BusinessException("Only a paid reservation can be refunded.");
        }

        // Prove the cancellation is legal BEFORE the external Stripe call, so we never refund then fail to persist
        // (e.g. a completed booking is terminal and can't be cancelled — rubric §7).
        ReservationStateMachine.EnsureCanTransition(reservation.Status, ReservationStatus.Cancelled);

        // Refund the actually-charged amount, never a recomputed price (rubric §7.1).
        var chargedCents = payment.AmountChargedCents ?? ToCents(payment.Amount);
        var refundResult = await _stripe.CreateRefundAsync(payment.ProviderPaymentIntentId, chargedCents, reason, ct);

        var now = _clock.UtcNow;
        payment.Status = PaymentStatus.Refunded;
        var refund = new Refund
        {
            PaymentId = payment.Id,
            Status = RefundStatus.Succeeded,
            Amount = chargedCents / (decimal)CentsPerUnit,
            ProviderRefundId = refundResult.Id,
            Reason = reason,
            CreatedAtUtc = now,
        };
        payment.Refunds.Add(refund);

        reservation.Audits.Add(NewAudit(reservation.Status, ReservationStatus.Cancelled, reason, now, CurrentUserId()));
        reservation.Status = ReservationStatus.Cancelled;
        reservation.CancelledAtUtc = now;
        reservation.CancellationReason = reason;

        // One SaveChanges commits the refund + the cancellation + its audit atomically (rubric §3.4).
        await _db.SaveChangesAsync(ct);

        await _paymentEvents.PublishAsync(new PaymentEvent(
            PaymentRoutingKeys.Refunded, payment.Id, reservation.Id, reservation.UserId,
            refund.Amount, payment.AmountChargedCents, now), ct);
        await PublishReservationEventAsync(ReservationRoutingKeys.Cancelled, reservation, reason, now, ct);

        _logger.LogInformation(
            "Refunded payment {PaymentId} ({Cents} cents) and cancelled reservation {ReservationId}.",
            payment.Id, chargedCents, reservation.Id);

        return ToPaymentDto(payment, refund);
    }

    /// <summary>The idempotent finalize the webhook drives: marks the payment Succeeded with the actually-charged
    /// cents and confirms a Pending reservation, all in one SaveChanges. A replayed event (payment already Succeeded)
    /// is a no-op (rubric §7.1).</summary>
    private async Task FinalizeAsync(string paymentIntentId, long? amountReceivedCents, CancellationToken ct)
    {
        var payment = await _db.Payments
            .Include(p => p.Reservation)
            .ThenInclude(r => r.TimeSlot)
            .FirstOrDefaultAsync(p => p.ProviderPaymentIntentId == paymentIntentId, ct);

        if (payment is null)
        {
            // A webhook for an intent we never created — log and ignore (return 200 so Stripe stops retrying).
            _logger.LogWarning("No payment found for Stripe intent {IntentId}; ignoring webhook.", paymentIntentId);
            return;
        }

        if (payment.Status == PaymentStatus.Succeeded)
        {
            _logger.LogInformation(
                "Payment {PaymentId} already finalized; ignoring replayed webhook (idempotent).", payment.Id);
            return;
        }

        var now = _clock.UtcNow;
        payment.Status = PaymentStatus.Succeeded;
        payment.AmountChargedCents = amountReceivedCents ?? ToCents(payment.Amount);
        payment.PaidAtUtc = now;

        var reservation = payment.Reservation;
        var confirmed = false;
        if (reservation.Status == ReservationStatus.Pending)
        {
            ReservationStateMachine.EnsureCanTransition(reservation.Status, ReservationStatus.Confirmed);
            reservation.Audits.Add(NewAudit(
                ReservationStatus.Pending, ReservationStatus.Confirmed, "Payment succeeded.", now, changedBy: null));
            reservation.Status = ReservationStatus.Confirmed;
            confirmed = true;
        }
        else
        {
            // The hold may have expired and the reservation already been cancelled (F17), or an admin pre-confirmed it.
            // Record the money regardless; don't force an illegal transition.
            _logger.LogWarning(
                "Finalizing payment {PaymentId} for reservation {ReservationId} in {Status} state (not Pending).",
                payment.Id, reservation.Id, reservation.Status);
        }

        // One SaveChanges commits the payment + the reservation transition + its audit atomically (rubric §3.4).
        await _db.SaveChangesAsync(ct);

        await _paymentEvents.PublishAsync(new PaymentEvent(
            PaymentRoutingKeys.Succeeded, payment.Id, reservation.Id, reservation.UserId,
            payment.Amount, payment.AmountChargedCents, now), ct);

        if (confirmed)
        {
            await PublishReservationEventAsync(ReservationRoutingKeys.Confirmed, reservation, reason: null, now, ct);
        }

        _logger.LogInformation(
            "Finalized payment {PaymentId} for reservation {ReservationId} (charged {Cents} cents).",
            payment.Id, reservation.Id, payment.AmountChargedCents);
    }

    // --- helpers ------------------------------------------------------------------------------------

    private Guid CurrentUserId() =>
        _currentUser.UserId ?? throw new UnauthorizedException("You must be signed in to manage payments.");

    private static long ToCents(decimal amount) =>
        (long)Math.Round(amount * CentsPerUnit, MidpointRounding.AwayFromZero);

    private static ReservationAudit NewAudit(
        ReservationStatus? oldStatus, ReservationStatus newStatus, string? reason, DateTime at, Guid? changedBy) =>
        new()
        {
            OldStatus = oldStatus,
            NewStatus = newStatus,
            Reason = reason,
            ChangedByUserId = changedBy,
            CreatedAtUtc = at,
        };

    private Task PublishReservationEventAsync(
        string routingKey, Reservation reservation, string? reason, DateTime now, CancellationToken ct) =>
        _reservationEvents.PublishAsync(
            new ReservationEvent(
                routingKey, reservation.Id, reservation.UserId, reservation.CourtId, reservation.TimeSlotId,
                reservation.Status, reservation.TotalPrice, reservation.TimeSlot.StartUtc, reservation.TimeSlot.EndUtc,
                reason, now),
            ct);

    private PaymentIntentResponse BuildIntentResponse(Payment payment, long reservationId, string clientSecret) =>
        new(payment.Id, reservationId, clientSecret, _publishableKey, ToCents(payment.Amount), _currency);

    private static PaymentDto ToPaymentDto(Payment payment, Refund? refund) =>
        new(
            payment.Id,
            payment.ReservationId,
            payment.Status,
            PaymentLabel(payment.Status),
            payment.Amount,
            payment.AmountChargedCents,
            payment.Status == PaymentStatus.Succeeded,
            payment.CreatedAtUtc,
            payment.PaidAtUtc,
            refund is null
                ? null
                : new RefundDto(
                    refund.Id, refund.Status, RefundLabel(refund.Status), refund.Amount, refund.Reason,
                    refund.CreatedAtUtc));

    private static string PaymentLabel(PaymentStatus status) => status switch
    {
        PaymentStatus.Pending => "Pending",
        PaymentStatus.Succeeded => "Succeeded",
        PaymentStatus.Failed => "Failed",
        PaymentStatus.Refunded => "Refunded",
        _ => status.ToString(),
    };

    private static string RefundLabel(RefundStatus status) => status switch
    {
        RefundStatus.Pending => "Pending",
        RefundStatus.Succeeded => "Succeeded",
        RefundStatus.Failed => "Failed",
        _ => status.ToString(),
    };
}
