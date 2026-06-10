namespace Courtly.Domain.Entities;

/// <summary>A user's review and rating of a court following a reservation.</summary>
public class Review
{
    public long Id { get; set; }

    public long ReservationId { get; set; }
    public Reservation Reservation { get; set; } = null!;

    public long CourtId { get; set; }
    public Court Court { get; set; } = null!;

    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public int Rating { get; set; }
    public string? Comment { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
