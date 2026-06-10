namespace Courtly.Domain.Entities;

using Courtly.Domain.Enums;

/// <summary>A court booking made by a user for a specific time slot.</summary>
public class Reservation
{
    public long Id { get; set; }

    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public long CourtId { get; set; }
    public Court Court { get; set; } = null!;

    public long TimeSlotId { get; set; }
    public TimeSlot TimeSlot { get; set; } = null!;

    public ReservationStatus Status { get; set; }

    public decimal TotalPrice { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime? HoldExpiresAtUtc { get; set; }

    public Payment? Payment { get; set; }
    public Review? Review { get; set; }
    public ICollection<ReservationAudit> Audits { get; set; } = new List<ReservationAudit>();
}
