namespace Courtly.Domain.Enums;

/// <summary>State of a Stripe payment for a reservation. Stored as int.</summary>
public enum PaymentStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
    Refunded = 3,

    /// <summary>
    /// The charge was captured at Stripe but could not be finalized normally (e.g. the webhook amount/currency
    /// did not match the reservation, or the reservation was no longer confirmable). The money is held and the
    /// payment is parked for staff/admin to resolve (typically a refund) — it is never treated as a clean success.
    /// </summary>
    RequiresReview = 4,
}
