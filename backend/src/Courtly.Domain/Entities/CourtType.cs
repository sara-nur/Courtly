namespace Courtly.Domain.Entities;

/// <summary>A category of court (e.g. indoor, outdoor, padel).</summary>
public class CourtType
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }
}
