namespace Courtly.Domain.Entities;

/// <summary>A user's bookmark of a court for quick access.</summary>
public class Favorite
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public long CourtId { get; set; }
    public Court Court { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
}
