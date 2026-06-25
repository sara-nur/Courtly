using Courtly.Application.Abstractions;
using Courtly.Application.Common.Exceptions;
using Courtly.Contracts.Court;
using Courtly.Domain.Enums;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Courtly.Application.Courts.Slots;

/// <summary>
/// Time slots &amp; availability (feature 13). The <c>TimeSlot</c> is the authoritative availability + price unit;
/// this service owns slot <b>generation</b> (the server computes each slot's time-of-day bucket and price — the
/// client never sends a price), the per-day <b>availability</b> view (free/taken, bucketed Morning/Afternoon/Evening),
/// and the admin <b>remove</b> actions. Every timestamp is UTC via <see cref="IClock"/>; reads are <c>AsNoTracking</c>
/// and projected to DTOs (never entities).
/// </summary>
/// <remarks>
/// <para><b>Bucketing + price.</b> A slot's bucket is derived from its start hour (Morning &lt; 12, Afternoon &lt; 17,
/// Evening otherwise) — the same thresholds the seeder uses. The price is the court's hourly rate scaled by the slot
/// duration, with an optional evening peak multiplier (server-owned; default <see cref="DefaultEveningPeakMultiplier"/>).</para>
/// <para><b>Duplicates.</b> Generation skips any start that already exists for the court (so re-generating an
/// overlapping range is idempotent); the <c>Unique(CourtId,StartUtc)</c> index is the hard DB-level guard behind it.</para>
/// <para><b>Maintenance.</b> Availability reuses the feature-12 exclusion query
/// (<see cref="IMaintenanceService.GetCourtIdsUnderMaintenanceAsync"/>): a court under maintenance for the queried day
/// yields no bookable slots.</para>
/// <para><b>Taken.</b> A slot is taken when an ACTIVE reservation (Pending/Confirmed) holds it; computed with a single
/// bounded query (no per-slot round-trip / N+1) and joined in memory.</para>
/// </remarks>
public sealed class TimeSlotService : ITimeSlotService
{
    private readonly CourtlyDbContext _db;
    private readonly IClock _clock;
    private readonly IMaintenanceService _maintenance;
    private readonly ILogger<TimeSlotService> _logger;

    // Server-owned slot rules (rubric §3.4: magic numbers → consts). Bucket thresholds mirror the seeder.
    private const int MorningEndHour = 12;   // [0, 12)  → Morning
    private const int AfternoonEndHour = 17; // [12, 17) → Afternoon; [17, 24) → Evening
    private const int MinutesPerHour = 60;
    private const decimal DefaultEveningPeakMultiplier = 1.2m;

    /// <summary>The three buckets in display order — every availability response carries all three (possibly empty).</summary>
    private static readonly TimeOfDayBucket[] BucketOrder =
        { TimeOfDayBucket.Morning, TimeOfDayBucket.Afternoon, TimeOfDayBucket.Evening };

    public TimeSlotService(
        CourtlyDbContext db, IClock clock, IMaintenanceService maintenance, ILogger<TimeSlotService> logger)
    {
        _db = db;
        _clock = clock;
        _maintenance = maintenance;
        _logger = logger;
    }

    public async Task<GenerateSlotsResult> GenerateAsync(
        long courtId, GenerateSlotsRequest request, CancellationToken ct = default)
    {
        var court = await _db.Courts.FirstOrDefaultAsync(c => c.Id == courtId, ct)
            ?? throw new NotFoundException($"Court {courtId} was not found.");

        var slotMinutes = request.SlotMinutes;
        var peakMultiplier = request.EveningPeakMultiplier ?? DefaultEveningPeakMultiplier;

        // The whole range in UTC, [FromDate 00:00, ToDate+1 00:00), for the existing-starts lookup.
        var rangeStartUtc = ToUtcMidnight(request.FromDate);
        var rangeEndUtc = ToUtcMidnight(request.ToDate.AddDays(1));

        // Existing slot starts in the range — used to skip duplicates so a re-generate is idempotent rather than a
        // unique-index violation. (The index remains the hard guard for concurrent generators.)
        var seenStarts = (await _db.TimeSlots.AsNoTracking()
                .Where(s => s.CourtId == courtId && s.StartUtc >= rangeStartUtc && s.StartUtc < rangeEndUtc)
                .Select(s => s.StartUtc)
                .ToListAsync(ct))
            .ToHashSet();

        var toAdd = new List<Domain.Entities.TimeSlot>();
        var skipped = 0;

        for (var date = request.FromDate; date <= request.ToDate; date = date.AddDays(1))
        {
            var dayStart = ToUtcMidnight(date);
            var openMinute = request.OpenHour * MinutesPerHour;
            var closeMinute = request.CloseHour * MinutesPerHour;

            // Step by slotMinutes while the whole slot fits before the close time.
            for (var minute = openMinute; minute + slotMinutes <= closeMinute; minute += slotMinutes)
            {
                var startUtc = dayStart.AddMinutes(minute);

                // seenStarts doubles as the "already created" set, so an existing OR just-queued start is skipped once.
                if (!seenStarts.Add(startUtc))
                {
                    skipped++;
                    continue;
                }

                var bucket = BucketForHour(startUtc.Hour);
                toAdd.Add(new Domain.Entities.TimeSlot
                {
                    CourtId = courtId,
                    StartUtc = startUtc,
                    EndUtc = startUtc.AddMinutes(slotMinutes),
                    Price = PriceFor(court.HourlyPrice, slotMinutes, bucket, peakMultiplier),
                    Bucket = bucket,
                    IsActive = true,
                });
            }
        }

        if (toAdd.Count > 0)
        {
            _db.TimeSlots.AddRange(toAdd);
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "Generated {Created} time slots on court {CourtId} ({Skipped} existing skipped) for {From}..{To}.",
            toAdd.Count, courtId, skipped, request.FromDate, request.ToDate);

        return new GenerateSlotsResult(toAdd.Count, skipped, request.FromDate, request.ToDate);
    }

    public async Task<DayAvailabilityDto> GetDayAvailabilityAsync(
        long courtId, DateOnly date, CancellationToken ct = default)
    {
        await EnsureCourtExistsAsync(courtId, ct);

        var dayStartUtc = ToUtcMidnight(date);
        var dayEndUtc = dayStartUtc.AddDays(1);

        // Maintenance exclusion (feature 12 reuse): a court under maintenance for the day yields no bookable slots.
        var underMaintenanceIds = await _maintenance.GetCourtIdsUnderMaintenanceAsync(dayStartUtc, dayEndUtc, ct);
        var underMaintenance = underMaintenanceIds.Contains(courtId);
        if (underMaintenance)
        {
            return new DayAvailabilityDto(courtId, date, true, EmptyBuckets());
        }

        // Active slots for the day, ordered by time.
        var slots = await _db.TimeSlots.AsNoTracking()
            .Where(s => s.CourtId == courtId && s.IsActive && s.StartUtc >= dayStartUtc && s.StartUtc < dayEndUtc)
            .OrderBy(s => s.StartUtc)
            .Select(s => new SlotRow(s.Id, s.StartUtc, s.EndUtc, s.Price, s.Bucket))
            .ToListAsync(ct);

        // "Taken" set: one bounded query (slot ids → active reservations), joined in memory (no N+1).
        var slotIds = slots.Select(s => s.Id).ToList();
        var takenIds = (await _db.Reservations.AsNoTracking()
                .Where(r => slotIds.Contains(r.TimeSlotId)
                            && (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed))
                .Select(r => r.TimeSlotId)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();

        return new DayAvailabilityDto(courtId, date, false, GroupIntoBuckets(slots, takenIds));
    }

    public async Task RemoveSlotAsync(long courtId, long slotId, CancellationToken ct = default)
    {
        var slot = await _db.TimeSlots.FirstOrDefaultAsync(s => s.Id == slotId && s.CourtId == courtId, ct)
            ?? throw new NotFoundException($"Time slot {slotId} was not found on court {courtId}.");

        if (await HasActiveReservationAsync(slotId, ct))
        {
            throw new BusinessException(
                "This slot can't be removed because it has an active booking (a pending or confirmed reservation).");
        }

        // A slot referenced by historical (cancelled/completed) reservations is FK-restricted — soft-remove it so it
        // leaves the bookable list while the reservation history is preserved. An unreferenced slot is hard-deleted.
        if (await _db.Reservations.AnyAsync(r => r.TimeSlotId == slotId, ct))
        {
            slot.IsActive = false;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Deactivated referenced time slot {SlotId} on court {CourtId}.", slotId, courtId);
            return;
        }

        _db.TimeSlots.Remove(slot);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Deleted time slot {SlotId} on court {CourtId}.", slotId, courtId);
    }

    public async Task<RemoveSlotsResult> RemoveDayAsync(long courtId, DateOnly date, CancellationToken ct = default)
    {
        await EnsureCourtExistsAsync(courtId, ct);

        var dayStartUtc = ToUtcMidnight(date);
        var dayEndUtc = dayStartUtc.AddDays(1);

        var slots = await _db.TimeSlots
            .Where(s => s.CourtId == courtId && s.StartUtc >= dayStartUtc && s.StartUtc < dayEndUtc)
            .ToListAsync(ct);
        if (slots.Count == 0)
        {
            return new RemoveSlotsResult(0, 0);
        }

        var slotIds = slots.Select(s => s.Id).ToList();

        // Two set queries (no per-slot round-trip): actively-booked ids (blocked) and any-history ids (soft-remove).
        var activeIds = (await _db.Reservations.AsNoTracking()
                .Where(r => slotIds.Contains(r.TimeSlotId)
                            && (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed))
                .Select(r => r.TimeSlotId)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();
        var historicalIds = (await _db.Reservations.AsNoTracking()
                .Where(r => slotIds.Contains(r.TimeSlotId))
                .Select(r => r.TimeSlotId)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();

        var removed = 0;
        foreach (var slot in slots)
        {
            if (activeIds.Contains(slot.Id))
            {
                continue; // keep — actively booked
            }

            if (historicalIds.Contains(slot.Id))
            {
                slot.IsActive = false; // soft-remove (preserve FK history)
            }
            else
            {
                _db.TimeSlots.Remove(slot);
            }
            removed++;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Removed {Removed} slots on court {CourtId} for {Date} ({Blocked} kept for active bookings).",
            removed, courtId, date, activeIds.Count);

        return new RemoveSlotsResult(removed, activeIds.Count);
    }

    private Task<bool> HasActiveReservationAsync(long slotId, CancellationToken ct) =>
        _db.Reservations.AnyAsync(r => r.TimeSlotId == slotId
            && (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed), ct);

    /// <summary>Verifies the court exists so a missing court is a clean 404, not an empty availability.</summary>
    private async Task EnsureCourtExistsAsync(long courtId, CancellationToken ct)
    {
        if (!await _db.Courts.AnyAsync(c => c.Id == courtId, ct))
        {
            throw new NotFoundException($"Court {courtId} was not found.");
        }
    }

    /// <summary>Server-owned price: hourly rate × duration, with the evening peak multiplier on Evening slots only,
    /// rounded to 2 decimals (matches the seeder).</summary>
    private static decimal PriceFor(decimal hourlyPrice, int slotMinutes, TimeOfDayBucket bucket, decimal peakMultiplier)
    {
        var basePrice = hourlyPrice * (slotMinutes / (decimal)MinutesPerHour);
        var multiplier = bucket == TimeOfDayBucket.Evening ? peakMultiplier : 1m;
        return decimal.Round(basePrice * multiplier, 2);
    }

    /// <summary>Maps a start hour to its time-of-day bucket (same thresholds as the seeder).</summary>
    private static TimeOfDayBucket BucketForHour(int hour) => hour switch
    {
        < MorningEndHour => TimeOfDayBucket.Morning,
        < AfternoonEndHour => TimeOfDayBucket.Afternoon,
        _ => TimeOfDayBucket.Evening,
    };

    private static IReadOnlyList<AvailabilityBucketDto> GroupIntoBuckets(
        IReadOnlyList<SlotRow> rows, HashSet<long> takenIds) =>
        BucketOrder.Select(b => new AvailabilityBucketDto(
                b,
                BucketLabel(b),
                rows.Where(r => r.Bucket == b)
                    .Select(r => new AvailabilitySlotDto(r.Id, r.StartUtc, r.EndUtc, r.Price, takenIds.Contains(r.Id)))
                    .ToList()))
            .ToList();

    private static IReadOnlyList<AvailabilityBucketDto> EmptyBuckets() =>
        BucketOrder.Select(b => new AvailabilityBucketDto(b, BucketLabel(b), Array.Empty<AvailabilitySlotDto>()))
            .ToList();

    /// <summary>The human-readable bucket label sent on the wire (so the client never maps the raw enum).</summary>
    private static string BucketLabel(TimeOfDayBucket bucket) => bucket switch
    {
        TimeOfDayBucket.Morning => "Morning",
        TimeOfDayBucket.Afternoon => "Afternoon",
        TimeOfDayBucket.Evening => "Evening",
        _ => bucket.ToString(),
    };

    /// <summary>Midnight (UTC) of a <see cref="DateOnly"/> — the canonical UTC anchor for a day's slots.</summary>
    private static DateTime ToUtcMidnight(DateOnly date) =>
        new(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Provider-agnostic intermediate for the availability projection; <c>IsTaken</c> is joined in memory.</summary>
    private sealed record SlotRow(long Id, DateTime StartUtc, DateTime EndUtc, decimal Price, TimeOfDayBucket Bucket);
}
