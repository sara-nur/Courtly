using Courtly.Domain.Enums;

namespace Courtly.Domain.Entities;

/// <summary>A bookable time window for a court with its own price and time-of-day bucket.</summary>
public class TimeSlot
{
    public long Id { get; set; }

    public long CourtId { get; set; }
    public Court Court { get; set; } = null!;

    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }

    public decimal Price { get; set; }
    public TimeOfDayBucket Bucket { get; set; }

    public bool IsActive { get; set; }
}
