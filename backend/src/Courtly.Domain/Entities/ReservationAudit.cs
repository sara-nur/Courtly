namespace Courtly.Domain.Entities;

using Courtly.Domain.Enums;

/// <summary>An audit log entry recording a reservation's status transition.</summary>
public class ReservationAudit
{
    public long Id { get; set; }

    public long ReservationId { get; set; }
    public Reservation Reservation { get; set; } = null!;

    public ReservationStatus? OldStatus { get; set; }
    public ReservationStatus NewStatus { get; set; }

    public string? Reason { get; set; }

    public Guid? ChangedByUserId { get; set; }
    public AppUser? ChangedBy { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
