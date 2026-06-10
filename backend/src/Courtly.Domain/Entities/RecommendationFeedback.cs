namespace Courtly.Domain.Entities;

/// <summary>User feedback on a recommended court, used to tune recommendations.</summary>
public class RecommendationFeedback
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public long CourtId { get; set; }
    public Court Court { get; set; } = null!;
    public bool IsHelpful { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
