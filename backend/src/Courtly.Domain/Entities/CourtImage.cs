namespace Courtly.Domain.Entities;

/// <summary>An image associated with a court, stored as raw bytes.</summary>
public class CourtImage
{
    public long Id { get; set; }

    public long CourtId { get; set; }
    public Court Court { get; set; } = null!;

    public byte[] Bytes { get; set; } = Array.Empty<byte>();
    public string ContentType { get; set; } = string.Empty;

    public bool IsPrimary { get; set; }
    public string? Caption { get; set; }
}
