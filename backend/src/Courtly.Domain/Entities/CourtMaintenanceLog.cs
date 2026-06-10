using Courtly.Domain.Enums;

namespace Courtly.Domain.Entities;

/// <summary>Records a maintenance event for a court, including status and timing.</summary>
public class CourtMaintenanceLog
{
    public long Id { get; set; }

    public long CourtId { get; set; }
    public Court Court { get; set; } = null!;

    public MaintenanceStatus Status { get; set; }
    public string Reason { get; set; } = string.Empty;

    public DateTime StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }

    public Guid? PerformedByUserId { get; set; }
    public AppUser? PerformedBy { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
