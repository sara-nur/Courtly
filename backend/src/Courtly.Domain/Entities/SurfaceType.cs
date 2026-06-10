namespace Courtly.Domain.Entities;

/// <summary>A type of court surface (e.g. clay, grass, hard).</summary>
public class SurfaceType
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }
}
