using Courtly.Application.Abstractions;
using Courtly.Application.Common.Exceptions;
using Courtly.Contracts.Court;
using Courtly.Domain.Enums;
using Courtly.Infrastructure.Configuration;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Courtly.Application.Courts.Slots;

/// <summary>
/// Time slots &amp; availability (feature 13). The <c>TimeSlot</c> is the authoritative availability + price unit;
/// this service owns slot <b>generation</b> (the server computes each slot's time-of-day bucket and price — the
/// client never sends a price), the per-day <b>availability</b> view (free/taken, bucketed Morning/Afternoon/Evening),
/// and the admin <b>remove</b> actions. Every timestamp is UTC via <see cref="IClock"/>; reads are <c>AsNoTracking</c>
/// and projected to DTOs (never entities).
/// </summary>
/// <remarks>
/// <para><b>Time zone.</b> Opening hours are business-<i>local</i> (the court's configured zone, default
/// Europe/Sarajevo). Generation interprets <c>date + OpenHour</c> as local time and converts it to UTC for storage, and
/// availability treats a day as the local business day — so an admin's 08:00 shows as 08:00 to users regardless of the
/// UTC offset or DST.</para>
/// <para><b>Bucketing + price.</b> A slot's bucket is derived from its <i>local</i> start hour (Morning &lt; 12,
/// Afternoon &lt; 17, Evening otherwise) — the same thresholds the seeder uses. The price is the court's hourly rate
/// scaled by the slot duration, with an optional evening peak multiplier (server-owned; default
/// <see cref="DefaultEveningPeakMultiplier"/>).</para>
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

    /// <summary>The courts' business-local time zone (configurable, default Europe/Sarajevo). Admin opening hours are
    /// interpreted in this zone, then converted to UTC for storage; the day window and buckets are the local day/hour.</summary>
    private readonly TimeZoneInfo _timeZone;

    // Server-owned slot rules (rubric §3.4: magic numbers → consts). Bucket thresholds mirror the seeder.
    private const int MorningEndHour = 12;   // [0, 12)  → Morning
    private const int AfternoonEndHour = 17; // [12, 17) → Afternoon; [17, 24) → Evening
    private const int MinutesPerHour = 60;
    private const decimal DefaultEveningPeakMultiplier = 1.2m;

    /// <summary>The three buckets in display order — every availability response carries all three (possibly empty).</summary>
    private static readonly TimeOfDayBucket[] BucketOrder =
        { TimeOfDayBucket.Morning, TimeOfDayBucket.Afternoon, TimeOfDayBucket.Evening };

    public TimeSlotService(
        CourtlyDbContext db,
        IClock clock,
        IMaintenanceService maintenance,
        IOptions<LocalizationOptions> localization,
        ILogger<TimeSlotService> logger)
    {
        _db = db;
        _clock = clock;
        _maintenance = maintenance;
        _logger = logger;
        _timeZone = ResolveTimeZone(localization.Value.TimeZoneId, logger);
    }

    /// <summary>Resolves the configured IANA time zone, falling back to UTC (with an error log) if it can't be found —
    /// a misconfiguration degrades to the old UTC behaviour rather than 500-ing every slot request.</summary>
    private static TimeZoneInfo ResolveTimeZone(string timeZoneId, ILogger<TimeSlotService> logger)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            logger.LogError(
                ex, "Court time zone '{TimeZoneId}' could not be resolved; falling back to UTC.", timeZoneId);
            return TimeZoneInfo.Utc;
        }
    }

    public async Task<GenerateSlotsResult> GenerateAsync(
        long courtId, GenerateSlotsRequest request, CancellationToken ct = default)
    {
        var court = await _db.Courts.FirstOrDefaultAsync(c => c.Id == courtId, ct)
            ?? throw new NotFoundException($"Court {courtId} was not found.");

        var slotMinutes = request.SlotMinutes;
        var peakMultiplier = request.EveningPeakMultiplier ?? DefaultEveningPeakMultiplier;

        // The whole range as UTC instants covering the local business days [FromDate 00:00, ToDate+1 00:00) local,
        // for the existing-starts lookup.
        var rangeStartUtc = LocalDayStartUtc(request.FromDate);
        var rangeEndUtc = LocalDayStartUtc(request.ToDate.AddDays(1));

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
            var openMinute = request.OpenHour * MinutesPerHour;
            var closeMinute = request.CloseHour * MinutesPerHour;

            // Step by slotMinutes while the whole slot fits before the close time.
            for (var minute = openMinute; minute + slotMinutes <= closeMinute; minute += slotMinutes)
            {
                // The admin's opening hour is business-LOCAL time — interpret (date + minute) in the court's zone and
                // convert to UTC for storage, so 08:00 entered for a BiH court is 08:00 local, not 08:00 UTC.
                var startUtc = LocalSlotStartUtc(date, minute);

                // seenStarts doubles as the "already created" set, so an existing OR just-queued start is skipped once.
                if (!seenStarts.Add(startUtc))
                {
                    skipped++;
                    continue;
                }

                // Bucket from the LOCAL hour (the admin's intended hour), never the UTC hour.
                var bucket = BucketForHour(minute / MinutesPerHour);
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

        // The day is the court's LOCAL business day, converted to UTC instants (handles DST-length days correctly).
        var dayStartUtc = LocalDayStartUtc(date);
        var dayEndUtc = LocalDayStartUtc(date.AddDays(1));

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

        // The day is the court's LOCAL business day, converted to UTC instants (handles DST-length days correctly).
        var dayStartUtc = LocalDayStartUtc(date);
        var dayEndUtc = LocalDayStartUtc(date.AddDays(1));

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

    /// <summary>The UTC instant of local midnight for a <see cref="DateOnly"/> in the court's time zone — the anchor
    /// for a local business day (used for day windows and generation range bounds).</summary>
    private DateTime LocalDayStartUtc(DateOnly date) => LocalToUtc(date, minutes: 0);

    /// <summary>The UTC instant of a local time <paramref name="minutes"/> minutes past midnight on
    /// <paramref name="date"/> in the court's time zone — used to place each generated slot's start.</summary>
    private DateTime LocalSlotStartUtc(DateOnly date, int minutes) => LocalToUtc(date, minutes);

    /// <summary>Interprets (<paramref name="date"/> midnight + <paramref name="minutes"/>) as an unspecified LOCAL time
    /// in the court's zone and converts it to UTC. Each slot is converted independently so DST transitions are handled
    /// per-instant rather than assuming a fixed 24h day.</summary>
    private DateTime LocalToUtc(DateOnly date, int minutes)
    {
        var local = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Unspecified)
            .AddMinutes(minutes);
        return TimeZoneInfo.ConvertTimeToUtc(local, _timeZone);
    }

    /// <summary>Provider-agnostic intermediate for the availability projection; <c>IsTaken</c> is joined in memory.</summary>
    private sealed record SlotRow(long Id, DateTime StartUtc, DateTime EndUtc, decimal Price, TimeOfDayBucket Bucket);
}
