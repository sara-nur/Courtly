using Courtly.Contracts.Payments;

namespace Courtly.Application.Payments;

/// <summary>
/// The payments engine (feature 16, Stripe server-side). Owns the money flow end-to-end on the server: it creates a
/// PaymentIntent for a Pending reservation (amount from the catalog, ownership from the JWT), finalizes the charge
/// idempotently from the Stripe webhook (flipping the reservation Pending → Confirmed), and issues admin refunds on the
/// actually-charged amount. The client never records success (rubric §7.1).
/// </summary>
public interface IPaymentService
{
    /// <summary>Creates (or re-fetches) the Stripe PaymentIntent for the caller's Pending, unpaid reservation and
    /// returns what the in-app PaymentSheet needs. Verifies ownership from the JWT and computes the amount from the
    /// reservation's server-owned price.</summary>
    Task<PaymentIntentResponse> CreateIntentAsync(CreatePaymentIntentRequest request, CancellationToken ct = default);

    /// <summary>Verifies + processes a Stripe webhook. On <c>payment_intent.succeeded</c> it idempotently finalizes the
    /// payment (Succeeded + actually-charged cents) and confirms the reservation; a replayed event is a no-op.</summary>
    Task ProcessWebhookAsync(string payloadJson, string signatureHeader, CancellationToken ct = default);

    /// <summary>Refunds a paid reservation (admin/staff): refunds the actually-charged amount via Stripe, marks the
    /// payment Refunded and cancels the reservation with the supplied reason.</summary>
    Task<PaymentDto> RefundAsync(long reservationId, RefundReservationRequest request, CancellationToken ct = default);
}
