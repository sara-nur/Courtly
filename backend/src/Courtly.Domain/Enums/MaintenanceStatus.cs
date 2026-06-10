namespace Courtly.Domain.Enums;

/// <summary>State of a court maintenance window. A court with an active (Scheduled/InProgress) window is unavailable for booking and excluded from analytics. Stored as int.</summary>
public enum MaintenanceStatus
{
    Scheduled = 0,
    InProgress = 1,
    Completed = 2,
    Cancelled = 3,
}
