using Courtly.Domain.Enums;

namespace Courtly.Domain.Entities;

/// <summary>A recorded court-search performed by a user, used for recommendations and recall.</summary>
public class SearchHistory
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public long? SurfaceTypeId { get; set; }
    public SurfaceType? SurfaceType { get; set; }
    public long? CourtTypeId { get; set; }
    public CourtType? CourtType { get; set; }
    public TimeOfDayBucket? Bucket { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public bool? IndoorOnly { get; set; }
    public string? RawQuery { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
