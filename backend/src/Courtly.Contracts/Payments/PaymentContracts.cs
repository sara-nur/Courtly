using Courtly.Domain.Enums;

namespace Courtly.Contracts.Payments;

/// <summary>
/// Payment engine contracts (feature 16, Stripe server-side). A reservation has at most one <c>Payment</c> (the 1:1
/// unique FK is the double-pay guard); the payment is finalized on the server by the Stripe webhook, never recorded by
/// the client (rubric §7.1). DTOs only on the wire — entities never leave the service.
/// </summary>
/// <remarks>
/// The server owns the amount end-to-end: the charge is the reservation's server-owned <c>TotalPrice</c> (the client
/// never sends a price), and a refund is issued on the <b>actually-charged</b> <c>AmountChargedCents</c>, never a
/// recomputed price.
/// </remarks>

/// <summary>Starts paying a reservation. The client sends only the reservation id; the server verifies ownership from
/// the JWT, that the reservation is Pending and unpaid, computes the amount from the catalog, and creates a Stripe
/// PaymentIntent. Never carries an amount (rubric §7.1 — server owns the price).</summary>
public sealed record CreatePaymentIntentRequest(long ReservationId);

/// <summary>What the client (feature 26 PaymentSheet) needs to confirm the charge in-app: the PaymentIntent
/// <see cref="ClientSecret"/>, the <see cref="PublishableKey"/> for the Stripe SDK, and the server-owned
/// <see cref="AmountCents"/>/<see cref="Currency"/> for display. <see cref="PaymentId"/> is our row's id for polling
/// <c>IsPaid</c>.</summary>
public sealed record PaymentIntentResponse(
    long PaymentId,
    long ReservationId,
    string ClientSecret,
    string PublishableKey,
    long AmountCents,
    string Currency);

/// <summary>Admin/staff refund of a paid reservation (rubric §7.1 refund + §7 "ne otkazati plaćeno bez toka za
/// refund"). A <see cref="Reason"/> is required (it rides along to the cancellation audit + notification). The amount
/// is never supplied — the server refunds the actually-charged cents.</summary>
public sealed record RefundReservationRequest(string Reason);

/// <summary>One refund issued against a payment.</summary>
public sealed record RefundDto(
    long Id,
    RefundStatus Status,
    string StatusName,
    decimal Amount,
    string? Reason,
    DateTime CreatedAtUtc);

/// <summary>A payment's full view (the refund endpoint's response). <see cref="IsPaid"/> is true only when
/// <see cref="Status"/> is <c>Succeeded</c> — it hides the pay button on the client (rubric §7.1).</summary>
public sealed record PaymentDto(
    long Id,
    long ReservationId,
    PaymentStatus Status,
    string StatusName,
    decimal Amount,
    long? AmountChargedCents,
    bool IsPaid,
    DateTime CreatedAtUtc,
    DateTime? PaidAtUtc,
    RefundDto? Refund);
