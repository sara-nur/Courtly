namespace Courtly.Domain.Entities;

/// <summary>Join entity linking a court to an amenity with optional descriptive payload.</summary>
public class CourtAmenity
{
    public long Id { get; set; }

    public long CourtId { get; set; }
    public Court Court { get; set; } = null!;

    public long AmenityId { get; set; }
    public Amenity Amenity { get; set; } = null!;

    public string? Note { get; set; }
    public bool IsHighlighted { get; set; }
}
