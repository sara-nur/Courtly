using Courtly.Domain.Enums;

namespace Courtly.Contracts.Court;

/// <summary>
/// Time slots &amp; availability contracts (feature 13). The <c>TimeSlot</c> is the authoritative availability + price
/// unit: the server owns slot generation and the price (the client never sends a price). DTOs only on the wire —
/// entities never leave the service.
/// </summary>
/// <remarks>
/// <see cref="GenerateSlotsRequest"/> asks the server to materialize hourly (or N-minute) slots for a court over a
/// date range and a daily open/close window; the server computes each slot's time-of-day bucket and price (with an
/// optional evening peak premium) and skips any start that already exists (the <c>Unique(CourtId,StartUtc)</c> index
/// is the hard DB-level guard). <see cref="DayAvailabilityDto"/> answers "what can be booked on this day", grouped
/// into Morning/Afternoon/Evening with each slot flagged free/taken; a court under maintenance for the day yields no
/// bookable slots. The remove contracts drive the admin "remove a slot / a whole day" management actions.
/// </remarks>
public sealed record GenerateSlotsRequest(
    DateOnly FromDate,
    DateOnly ToDate,
    int OpenHour,
    int CloseHour,
    int SlotMinutes = 60,
    decimal? EveningPeakMultiplier = null);

/// <summary>Summary of a generation run: how many slots were created and how many existing starts were skipped
/// (so re-generating over an overlapping range is idempotent, not a duplicate-key error).</summary>
public sealed record GenerateSlotsResult(
    int CreatedCount,
    int SkippedCount,
    DateOnly FromDate,
    DateOnly ToDate);

/// <summary>One bookable slot in the availability view: server-owned <see cref="Price"/>, and <see cref="IsTaken"/>
/// when an ACTIVE reservation (Pending/Confirmed) holds it.</summary>
public sealed record AvailabilitySlotDto(
    long Id,
    DateTime StartUtc,
    DateTime EndUtc,
    decimal Price,
    bool IsTaken);

/// <summary>One time-of-day group (Morning/Afternoon/Evening) of a day's slots. <see cref="BucketName"/> is the human
/// label so the client never maps the raw enum.</summary>
public sealed record AvailabilityBucketDto(
    TimeOfDayBucket Bucket,
    string BucketName,
    IReadOnlyList<AvailabilitySlotDto> Slots);

/// <summary>A court's availability for a single day: the three time-of-day buckets (always present, in order), each
/// with its free/taken slots. <see cref="IsCourtUnderMaintenance"/> is true (and the buckets empty) when the court
/// has an OPEN maintenance window covering the day — a maintenance court yields no bookable slots (feature 12
/// exclusion, reused here).</summary>
public sealed record DayAvailabilityDto(
    long CourtId,
    DateOnly Date,
    bool IsCourtUnderMaintenance,
    IReadOnlyList<AvailabilityBucketDto> Buckets);

/// <summary>Result of removing a day's slots: how many were removed and how many were kept because they have an
/// active booking (surfaced to the admin rather than silently skipped).</summary>
public sealed record RemoveSlotsResult(
    int RemovedCount,
    int BlockedCount);
