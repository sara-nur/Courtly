using Courtly.Domain.Enums;

namespace Courtly.Contracts.Messaging;

/// <summary>
/// RabbitMQ routing keys for reservation lifecycle events on the <c>courtly.events</c> topic exchange. Kept in
/// <c>Courtly.Contracts</c> so the API (publisher) and the Worker (consumer, feature 17) share one source of truth and
/// can't drift (rubric §3.4: no duplicated magic strings).
/// </summary>
public static class ReservationRoutingKeys
{
    public const string Created = "reservation.created";
    public const string Confirmed = "reservation.confirmed";
    public const string Cancelled = "reservation.cancelled";
    public const string Completed = "reservation.completed";
    public const string Rescheduled = "reservation.rescheduled";
}

/// <summary>
/// A reservation lifecycle event raised on every state transition (created/confirmed/cancelled/completed). The
/// reservation engine (feature 14) publishes these; the Worker consumes them later (feature 17) to send emails and
/// persist + push notifications. Carries everything a consumer needs (ids, status, price, slot window, the cancellation
/// reason) so the Worker need not call back into the API. Self-contained + serializable — no entity references.
/// </summary>
public sealed record ReservationEvent(
    string RoutingKey,
    long ReservationId,
    Guid UserId,
    long CourtId,
    long TimeSlotId,
    ReservationStatus Status,
    decimal TotalPrice,
    DateTime SlotStartUtc,
    DateTime SlotEndUtc,
    string? Reason,
    DateTime OccurredAtUtc);
