using Courtly.Contracts.Court;

namespace Courtly.Application.Abstractions;

/// <summary>
/// Time slots &amp; availability (feature 13). Owns server-side slot generation (server-owned price + time-of-day
/// bucket, optional evening peak), the per-day availability query the booking flow (F25) reuses, and the admin
/// remove actions. A court under maintenance (feature 12) yields no bookable slots for the affected day.
/// </summary>
public interface ITimeSlotService
{
    /// <summary>Generates the court's bookable slots over a date range and a daily open/close window. The server owns
    /// each slot's price (hourly rate × duration, with an optional evening peak) and bucket; existing starts are
    /// skipped so re-generating an overlapping range is idempotent rather than a duplicate-key error.</summary>
    Task<GenerateSlotsResult> GenerateAsync(long courtId, GenerateSlotsRequest request, CancellationToken ct = default);

    /// <summary>One day's availability for a court: active slots grouped into Morning/Afternoon/Evening, each flagged
    /// free or taken (an active Pending/Confirmed reservation). A court under maintenance for the day yields no
    /// bookable slots.</summary>
    Task<DayAvailabilityDto> GetDayAvailabilityAsync(long courtId, DateOnly date, CancellationToken ct = default);

    /// <summary>Removes one slot: blocked (409) when it has an active booking; otherwise soft-removed (deactivated)
    /// when reservation history references it, or hard-deleted when it is unreferenced.</summary>
    Task RemoveSlotAsync(long courtId, long slotId, CancellationToken ct = default);

    /// <summary>Removes a whole day's removable slots, keeping any with an active booking and reporting the counts.</summary>
    Task<RemoveSlotsResult> RemoveDayAsync(long courtId, DateOnly date, CancellationToken ct = default);
}
