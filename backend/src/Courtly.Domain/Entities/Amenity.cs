namespace Courtly.Domain.Entities;

/// <summary>An amenity offered at a venue (e.g. parking, showers).</summary>
public class Amenity
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? IconKey { get; set; }
}
