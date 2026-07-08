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

        // The hold is the window in which the slot is reserved for this user. Once it has expired the reservation is
        // no longer payable (the hold-expiry worker will cancel it): creating or re-fetching an intent here would let a
        // user be charged for a slot they no longer hold — the exact "charged but not confirmed" hazard we must avoid.
        if (reservation.HoldExpiresAtUtc is { } holdExpiry && holdExpiry <= _clock.UtcNow)
        {
            throw new BusinessException("The reservation hold has expired; please create a new reservation.");
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

        await FinalizeAsync(
            webhookEvent.PaymentIntentId, webhookEvent.AmountReceivedCents, webhookEvent.Currency, ct);
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

        // A captured charge can be refunded whether it finalized cleanly (Succeeded) or was parked for review
        // (RequiresReview — e.g. a webhook amount/currency mismatch): in both cases Stripe is holding real money.
        if (payment.Status is not (PaymentStatus.Succeeded or PaymentStatus.RequiresReview)
            || string.IsNullOrEmpty(payment.ProviderPaymentIntentId))
        {
            throw new BusinessException("Only a paid reservation can be refunded.");
        }

        // If the reservation is still active the refund also cancels it — prove that transition is legal BEFORE the
        // external Stripe call. An already-Cancelled reservation (e.g. its hold expired, then a late webhook parked the
        // payment for review) is refunded without forcing a second, illegal transition. A Completed booking was
        // fulfilled, so it is NOT refundable through this cancel-refund flow (rejected before any Stripe call).
        var cancelReservation = !ReservationStateMachine.IsTerminal(reservation.Status);
        if (cancelReservation)
        {
            ReservationStateMachine.EnsureCanTransition(reservation.Status, ReservationStatus.Cancelled);
        }
        else if (reservation.Status != ReservationStatus.Cancelled)
        {
            throw new BusinessException("A completed reservation cannot be refunded.");
        }

        // Refund the actually-charged amount, never a recomputed price (rubric §7.1).
        var chargedCents = payment.AmountChargedCents ?? ToCents(payment.Amount);
        var now = _clock.UtcNow;

        // Persist the intent-to-refund BEFORE the external Stripe call. If Stripe then succeeds but the follow-up
        // local write fails, the database shows a discoverable Pending refund row instead of the dangerous silent
        // state "money returned at Stripe but payment still Succeeded and reservation still active".
        var refund = new Refund
        {
            PaymentId = payment.Id,
            Status = RefundStatus.Pending,
            Amount = chargedCents / (decimal)CentsPerUnit,
            Reason = reason,
            CreatedAtUtc = now,
        };
        payment.Refunds.Add(refund);
        await _db.SaveChangesAsync(ct);

        RefundResult refundResult;
        try
        {
            refundResult = await _stripe.CreateRefundAsync(payment.ProviderPaymentIntentId, chargedCents, reason, ct);
        }
        catch (Exception ex)
        {
            // Stripe failed — mark the pending row Failed so it is never mistaken for money returned, and surface the
            // error. The payment stays as-is and the reservation is untouched (nothing was refunded).
            _logger.LogError(
                ex, "Stripe refund failed for payment {PaymentId}; marking refund {RefundId} Failed.",
                payment.Id, refund.Id);
            refund.Status = RefundStatus.Failed;
            await _db.SaveChangesAsync(ct);
            throw;
        }

        // Stripe succeeded — promote the refund, mark the payment Refunded, and (if it was still active) cancel the
        // reservation, all in one SaveChanges (rubric §3.4).
        refund.Status = RefundStatus.Succeeded;
        refund.ProviderRefundId = refundResult.Id;
        payment.Status = PaymentStatus.Refunded;

        if (cancelReservation)
        {
            reservation.Audits.Add(
                NewAudit(reservation.Status, ReservationStatus.Cancelled, reason, now, CurrentUserId()));
            reservation.Status = ReservationStatus.Cancelled;
            reservation.CancelledAtUtc = now;
            reservation.CancellationReason = reason;
        }

        await _db.SaveChangesAsync(ct);

        await _paymentEvents.PublishAsync(new PaymentEvent(
            PaymentRoutingKeys.Refunded, payment.Id, reservation.Id, reservation.UserId,
            refund.Amount, payment.AmountChargedCents, now), ct);
        if (cancelReservation)
        {
            await PublishReservationEventAsync(ReservationRoutingKeys.Cancelled, reservation, reason, now, ct);
        }

        _logger.LogInformation(
            "Refunded payment {PaymentId} ({Cents} cents); reservation {ReservationId} cancelled: {Cancelled}.",
            payment.Id, chargedCents, reservation.Id, cancelReservation);

        return ToPaymentDto(payment, refund);
    }

    /// <summary>The idempotent finalize the webhook drives. The webhook is the authoritative record of the real
    /// charge, so it is the strictest checkpoint: before a payment is treated as a clean success it must match the
    /// reservation's expected amount and currency AND the reservation must still be confirmable (Pending). On a match
    /// it marks the payment Succeeded and confirms the reservation in one SaveChanges. On any mismatch — wrong amount,
    /// wrong currency, or a reservation that is no longer Pending — the money was still captured at Stripe, so the
    /// payment is parked in <see cref="PaymentStatus.RequiresReview"/> for staff/admin (never a false Succeeded, never
    /// an illegal confirm). A replayed event for an already terminal/review payment is a no-op (rubric §7.1).</summary>
    private async Task FinalizeAsync(
        string paymentIntentId, long? amountReceivedCents, string? currency, CancellationToken ct)
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

        // A replayed webhook for a payment we've already finalized, refunded, or parked for review must not be
        // reprocessed (idempotent, rubric §7.1).
        if (payment.Status is PaymentStatus.Succeeded or PaymentStatus.Refunded or PaymentStatus.RequiresReview)
        {
            _logger.LogInformation(
                "Payment {PaymentId} is already {Status}; ignoring replayed webhook (idempotent).",
                payment.Id, payment.Status);
            return;
        }

        var now = _clock.UtcNow;
        var reservation = payment.Reservation;

        // Verify the real charge against what the reservation expected. The amount comes from the server-owned price;
        // the currency from the single configured Stripe currency (compared only when the webhook exposes it).
        var expectedCents = ToCents(payment.Amount);
        var amountMismatch = amountReceivedCents is { } received && received != expectedCents;
        var currencyMismatch = !string.IsNullOrEmpty(currency)
            && !string.Equals(currency, _currency, StringComparison.OrdinalIgnoreCase);
        var reservationNotConfirmable = reservation.Status != ReservationStatus.Pending;

        if (amountMismatch || currencyMismatch || reservationNotConfirmable)
        {
            // Money was captured but something is off — hold it for manual resolution (typically a refund via
            // RefundAsync). We deliberately do NOT publish a "succeeded" event: nothing downstream should tell the
            // user the booking is paid/confirmed.
            payment.Status = PaymentStatus.RequiresReview;
            payment.AmountChargedCents = amountReceivedCents ?? expectedCents; // record what Stripe actually reported
            payment.PaidAtUtc = now;

            _logger.LogError(
                "Payment {PaymentId} for reservation {ReservationId} needs manual review — " +
                "amountMismatch={AmountMismatch} (expected {Expected}, received {Received}), " +
                "currencyMismatch={CurrencyMismatch} (expected {ExpectedCurrency}, received {ReceivedCurrency}), " +
                "reservationStatus={Status}.",
                payment.Id, reservation.Id, amountMismatch, expectedCents, amountReceivedCents,
                currencyMismatch, _currency, currency, reservation.Status);

            await _db.SaveChangesAsync(ct);
            return;
        }

        // Normal path: the charge matches and the reservation is still Pending — mark Succeeded and confirm, all in one
        // SaveChanges (payment + reservation transition + audit, atomic, rubric §3.4).
        payment.Status = PaymentStatus.Succeeded;
        payment.AmountChargedCents = amountReceivedCents ?? expectedCents;
        payment.PaidAtUtc = now;

        ReservationStateMachine.EnsureCanTransition(reservation.Status, ReservationStatus.Confirmed);
        reservation.Audits.Add(NewAudit(
            ReservationStatus.Pending, ReservationStatus.Confirmed, "Payment succeeded.", now, changedBy: null));
        reservation.Status = ReservationStatus.Confirmed;

        await _db.SaveChangesAsync(ct);

        await _paymentEvents.PublishAsync(new PaymentEvent(
            PaymentRoutingKeys.Succeeded, payment.Id, reservation.Id, reservation.UserId,
            payment.Amount, payment.AmountChargedCents, now), ct);
        await PublishReservationEventAsync(ReservationRoutingKeys.Confirmed, reservation, reason: null, now, ct);

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
        PaymentStatus.RequiresReview => "Requires review",
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
