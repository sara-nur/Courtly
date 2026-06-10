namespace Courtly.Domain.Entities;

using Courtly.Domain.Enums;

/// <summary>A payment record associated with a reservation.</summary>
public class Payment
{
    public long Id { get; set; }

    public long ReservationId { get; set; }
    public Reservation Reservation { get; set; } = null!;

    public PaymentStatus Status { get; set; }

    public decimal Amount { get; set; }
    public long? AmountChargedCents { get; set; }

    public string? ProviderPaymentIntentId { get; set; }
    public string? IdempotencyKey { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }

    public ICollection<Refund> Refunds { get; set; } = new List<Refund>();
}
