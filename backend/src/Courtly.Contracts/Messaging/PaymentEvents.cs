namespace Courtly.Contracts.Messaging;

/// <summary>
/// RabbitMQ routing keys for payment lifecycle events on the <c>courtly.events</c> topic exchange. Kept in
/// <c>Courtly.Contracts</c> (beside <see cref="ReservationRoutingKeys"/>) so the API (publisher) and the Worker
/// (consumer, feature 17) share one source of truth and can't drift (rubric §3.4: no duplicated magic strings).
/// </summary>
public static class PaymentRoutingKeys
{
    public const string Succeeded = "payment.succeeded";
    public const string Refunded = "payment.refunded";
}

/// <summary>
/// A payment lifecycle event raised by the payments engine (feature 16): <c>payment.succeeded</c> when the Stripe
/// webhook finalizes a charge, <c>payment.refunded</c> when an admin refund completes. The Worker consumes these later
/// (feature 17) to send the "payment received" / "refund issued" emails and push notifications, so the event carries
/// everything a consumer needs (ids, the catalog amount, the actually-charged cents) and never references an entity.
/// Self-contained + serializable.
/// </summary>
public sealed record PaymentEvent(
    string RoutingKey,
    long PaymentId,
    long ReservationId,
    Guid UserId,
    decimal Amount,
    long? AmountChargedCents,
    DateTime OccurredAtUtc);
