namespace Courtly.Domain.Entities;

using Courtly.Domain.Enums;

/// <summary>A refund issued against a payment.</summary>
public class Refund
{
    public long Id { get; set; }

    public long PaymentId { get; set; }
    public Payment Payment { get; set; } = null!;

    public RefundStatus Status { get; set; }

    public decimal Amount { get; set; }

    public string? ProviderRefundId { get; set; }
    public string? Reason { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
