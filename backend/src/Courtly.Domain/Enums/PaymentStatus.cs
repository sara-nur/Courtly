namespace Courtly.Domain.Enums;

/// <summary>State of a Stripe payment for a reservation. Stored as int.</summary>
public enum PaymentStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
    Refunded = 3,
}
