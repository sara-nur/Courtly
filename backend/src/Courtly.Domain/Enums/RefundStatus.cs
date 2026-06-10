namespace Courtly.Domain.Enums;

/// <summary>State of a refund issued against a payment. Stored as int.</summary>
public enum RefundStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
}
