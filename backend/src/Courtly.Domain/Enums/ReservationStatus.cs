namespace Courtly.Domain.Enums;

/// <summary>Lifecycle states of a reservation. Stored as int; Pending+Confirmed are the "active hold" states guarded by the filtered-unique slot index.</summary>
public enum ReservationStatus
{
    Pending = 0,
    Confirmed = 1,
    Cancelled = 2,
    Completed = 3,
}
