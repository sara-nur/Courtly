using Courtly.Application.Abstractions;
using Courtly.Contracts.Dashboard;
using Courtly.Domain.Entities;
using Courtly.Domain.Enums;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Courtly.Application.Dashboard;

/// <summary>
/// Dashboard analytics engine (feature 19). Turns the reservation (F14) and payment (F16) data into the admin
/// overview's KPIs, chart series and health banner. Every figure is computed by a single aggregate query at the
/// database (<c>Count</c>/<c>Sum</c>/<c>GroupBy</c> — never row-by-row, never filtered in memory; rubric §8.2), reads
/// are <c>AsNoTracking</c>, and the whole composite result is cached for a short TTL so the client's auto-refresh never
/// recomputes the heavy queries each tick (roadmap §7).
/// </summary>
/// <remarks>
/// <para><b>What counts.</b> Only <c>Confirmed</c>/<c>Completed</c> reservations count toward volume + occupancy
/// (Pending holds and Cancelled don't), and a court that is under maintenance anywhere across the analysis window is
/// excluded from every figure (the same exclusion set the booking path reuses — F12).</para>
/// <para><b>Revenue is net of refunds.</b> A refund flips its payment to <c>Refunded</c>, so summing only
/// <c>Succeeded</c> payments already nets refunds out — subtracting refund rows on top would double-count the loss.
/// Money is the <i>actually-charged</i> cents (rubric §7.1), divided by 100 once so the wire carries currency units.</para>
/// <para><b>Prior-period deltas.</b> Each KPI also reports the same metric over the equal-length window immediately
/// before the selected one, so the UI can render the ▲/▼% the mockup shows.</para>
/// </remarks>
public sealed class DashboardService : IDashboardService
{
    private const int CentsPerUnit = 100;
    private const int DefaultWindowDays = 30;
    private const int PopularCourtsCount = 5;
    private const int HoursPerDay = 24;

    // Health-banner thresholds (occupancy is a percentage; revenue is the prior-period % change).
    private const decimal HealthyOccupancyPct = 60m;
    private const decimal AtRiskOccupancyPct = 30m;
    private const double RevenueHealthyMinDeltaPct = -5d;   // revenue may dip up to 5% and still be "healthy"
    private const double RevenueAtRiskDeltaPct = -20d;      // a ≥20% drop is "at risk"

    private const string CacheKeyPrefix = "dashboard:metrics";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly CourtlyDbContext _db;
    private readonly IClock _clock;
    private readonly IMaintenanceService _maintenance;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DashboardService> _logger;

    public DashboardService(
        CourtlyDbContext db,
        IClock clock,
        IMaintenanceService maintenance,
        IMemoryCache cache,
        ILogger<DashboardService> logger)
    {
        _db = db;
        _clock = clock;
        _maintenance = maintenance;
        _cache = cache;
        _logger = logger;
    }

    public async Task<DashboardMetricsDto> GetMetricsAsync(DashboardFiltersQuery filter, CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = NormalizeWindow(filter);
        var cacheKey = $"{CacheKeyPrefix}:{fromUtc:O}:{toUtc:O}:{filter.CourtTypeId?.ToString() ?? "all"}";
        if (_cache.TryGetValue(cacheKey, out DashboardMetricsDto? cached) && cached is not null)
        {
            return cached;
        }

        var window = toUtc - fromUtc;
        var prevFrom = fromUtc - window;
        var prevTo = fromUtc;

        // One exclusion set covering BOTH windows, so the current and prior figures exclude the same courts and the
        // delta is apples-to-apples (F12 reuse — courts under maintenance are out of the dashboard, like booking).
        var maintenanceIds = await _maintenance.GetCourtIdsUnderMaintenanceAsync(prevFrom, toUtc, ct);

        var current = await ComputeWindowAsync(fromUtc, toUtc, filter.CourtTypeId, maintenanceIds, ct);
        var previous = await ComputeWindowAsync(prevFrom, prevTo, filter.CourtTypeId, maintenanceIds, ct);

        var revenueTrend = await RevenueTrendAsync(fromUtc, toUtc, filter.CourtTypeId, maintenanceIds, ct);
        var popularCourts = await PopularCourtsAsync(
            fromUtc, toUtc, filter.CourtTypeId, maintenanceIds, current.BookedSlots, ct);
        var peakHours = await PeakHoursAsync(fromUtc, toUtc, filter.CourtTypeId, maintenanceIds, ct);

        var revenue = new KpiDto(current.Revenue, previous.Revenue, Delta(current.Revenue, previous.Revenue));
        var dto = new DashboardMetricsDto(
            fromUtc,
            toUtc,
            new KpiDto(current.Reservations, previous.Reservations, Delta(current.Reservations, previous.Reservations)),
            revenue,
            new KpiDto(current.OccupancyPct, previous.OccupancyPct, Delta(current.OccupancyPct, previous.OccupancyPct)),
            new KpiDto(current.ActiveUsers, previous.ActiveUsers, Delta(current.ActiveUsers, previous.ActiveUsers)),
            revenueTrend,
            popularCourts,
            peakHours,
            BuildHealth(current.OccupancyPct, revenue.DeltaPercent));

        _cache.Set(cacheKey, dto, CacheTtl);
        _logger.LogInformation(
            "Computed dashboard metrics for {From:o}..{To:o} (courtType {CourtType}).",
            fromUtc, toUtc, filter.CourtTypeId);
        return dto;
    }

    // --- per-window scalar aggregates ---------------------------------------------------------------

    /// <summary>The four KPI totals for one window: counted reservations + active bookers (by booking time), net
    /// revenue (by paid time), and occupancy (booked vs available slots, by slot start). Each is one aggregate query.
    /// <see cref="WindowTotals.BookedSlots"/> doubles as the denominator for the popular-courts percentages.</summary>
    private async Task<WindowTotals> ComputeWindowAsync(
        DateTime from, DateTime to, long? courtTypeId, IReadOnlyList<long> maintenanceIds, CancellationToken ct)
    {
        var counted = CountedReservations(courtTypeId, maintenanceIds);

        var reservations = await counted
            .Where(r => r.CreatedAtUtc >= from && r.CreatedAtUtc < to)
            .CountAsync(ct);

        var activeUsers = await counted
            .Where(r => r.CreatedAtUtc >= from && r.CreatedAtUtc < to)
            .Select(r => r.UserId)
            .Distinct()
            .CountAsync(ct);

        var chargedCents = await CountedPayments(courtTypeId, maintenanceIds)
            .Where(p => p.PaidAtUtc >= from && p.PaidAtUtc < to)
            .SumAsync(p => p.AmountChargedCents ?? 0L, ct);

        var bookedSlots = await counted
            .Where(r => r.TimeSlot.StartUtc >= from && r.TimeSlot.StartUtc < to)
            .CountAsync(ct);

        var availableSlots = await CandidateSlots(courtTypeId, maintenanceIds)
            .Where(s => s.StartUtc >= from && s.StartUtc < to)
            .CountAsync(ct);

        var occupancy = availableSlots == 0
            ? 0m
            : Math.Round((decimal)bookedSlots / availableSlots * 100m, 1);

        return new WindowTotals(
            reservations, chargedCents / (decimal)CentsPerUnit, occupancy, activeUsers, bookedSlots);
    }

    // --- chart series (current window only) ---------------------------------------------------------

    /// <summary>Net revenue grouped by UTC day (single <c>GroupBy</c> on year/month/day — translatable on Npgsql),
    /// then zero-filled across the window so the line chart has a point for every day.</summary>
    private async Task<IReadOnlyList<RevenueTrendPointDto>> RevenueTrendAsync(
        DateTime from, DateTime to, long? courtTypeId, IReadOnlyList<long> maintenanceIds, CancellationToken ct)
    {
        var grouped = await CountedPayments(courtTypeId, maintenanceIds)
            .Where(p => p.PaidAtUtc >= from && p.PaidAtUtc < to)
            .GroupBy(p => new { p.PaidAtUtc!.Value.Year, p.PaidAtUtc!.Value.Month, p.PaidAtUtc!.Value.Day })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                g.Key.Day,
                Cents = g.Sum(p => p.AmountChargedCents ?? 0L),
            })
            .ToListAsync(ct);

        var byDay = grouped.ToDictionary(
            x => new DateTime(x.Year, x.Month, x.Day, 0, 0, 0, DateTimeKind.Utc), x => x.Cents);

        var points = new List<RevenueTrendPointDto>();
        for (var day = new DateTime(from.Year, from.Month, from.Day, 0, 0, 0, DateTimeKind.Utc); day < to; day = day.AddDays(1))
        {
            var cents = byDay.TryGetValue(day, out var c) ? c : 0L;
            points.Add(new RevenueTrendPointDto(day, cents / (decimal)CentsPerUnit));
        }

        return points;
    }

    /// <summary>The top courts by counted-reservation volume in the window (single <c>GroupBy</c> + <c>Take</c>), then
    /// one lookup over those ≤5 ids for their name + primary image url (no N+1, no raw ids on the wire).</summary>
    private async Task<IReadOnlyList<PopularCourtDto>> PopularCourtsAsync(
        DateTime from, DateTime to, long? courtTypeId, IReadOnlyList<long> maintenanceIds, int bookedTotal,
        CancellationToken ct)
    {
        var top = await CountedReservations(courtTypeId, maintenanceIds)
            .Where(r => r.TimeSlot.StartUtc >= from && r.TimeSlot.StartUtc < to)
            .GroupBy(r => r.CourtId)
            .Select(g => new { CourtId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.CourtId)
            .Take(PopularCourtsCount)
            .ToListAsync(ct);

        if (top.Count == 0)
        {
            return Array.Empty<PopularCourtDto>();
        }

        var ids = top.Select(t => t.CourtId).ToList();
        var courtInfo = await _db.Courts.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new
            {
                c.Id,
                c.Name,
                // Primary-flagged image first, else earliest by id — the same ordered subquery the catalog uses; the
                // URL is built in memory below (Npgsql can't translate the interpolated string).
                PrimaryImageId = c.Images
                    .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.Id)
                    .Select(i => (long?)i.Id)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);
        var infoById = courtInfo.ToDictionary(x => x.Id);

        return top
            .Select(t =>
            {
                var info = infoById[t.CourtId];
                var percentage = bookedTotal == 0 ? 0d : Math.Round((double)t.Count / bookedTotal * 100d, 1);
                var imageUrl = info.PrimaryImageId is long pid ? $"/api/images/{pid}" : null;
                return new PopularCourtDto(info.Name, imageUrl, t.Count, percentage);
            })
            .ToList();
    }

    /// <summary>Counted reservations grouped by the UTC hour their slot starts (single <c>GroupBy</c>), zero-filled to
    /// all 24 hours so the peak-hours chart is gapless.</summary>
    private async Task<IReadOnlyList<PeakHourPointDto>> PeakHoursAsync(
        DateTime from, DateTime to, long? courtTypeId, IReadOnlyList<long> maintenanceIds, CancellationToken ct)
    {
        var grouped = await CountedReservations(courtTypeId, maintenanceIds)
            .Where(r => r.TimeSlot.StartUtc >= from && r.TimeSlot.StartUtc < to)
            .GroupBy(r => r.TimeSlot.StartUtc.Hour)
            .Select(g => new { Hour = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var byHour = grouped.ToDictionary(x => x.Hour, x => x.Count);
        return Enumerable.Range(0, HoursPerDay)
            .Select(h => new PeakHourPointDto(h, byHour.TryGetValue(h, out var c) ? c : 0))
            .ToList();
    }

    // --- candidate-set query builders ---------------------------------------------------------------

    /// <summary>Reservations that count toward the dashboard: Confirmed/Completed, on an active court that is not under
    /// maintenance, optionally narrowed to one court type. The time predicate is added by each caller.</summary>
    private IQueryable<Reservation> CountedReservations(long? courtTypeId, IReadOnlyList<long> maintenanceIds)
    {
        var query = _db.Reservations.AsNoTracking().Where(r =>
            (r.Status == ReservationStatus.Confirmed || r.Status == ReservationStatus.Completed)
            && r.Court.IsActive
            && !maintenanceIds.Contains(r.CourtId));

        if (courtTypeId.HasValue)
        {
            query = query.Where(r => r.Court.CourtTypeId == courtTypeId.Value);
        }

        return query;
    }

    /// <summary>Succeeded payments on candidate courts (the net-revenue source — Refunded payments are excluded by the
    /// status filter). The paid-time predicate is added by each caller.</summary>
    private IQueryable<Payment> CountedPayments(long? courtTypeId, IReadOnlyList<long> maintenanceIds)
    {
        var query = _db.Payments.AsNoTracking().Where(p =>
            p.Status == PaymentStatus.Succeeded
            && p.PaidAtUtc != null
            && p.Reservation.Court.IsActive
            && !maintenanceIds.Contains(p.Reservation.CourtId));

        if (courtTypeId.HasValue)
        {
            query = query.Where(p => p.Reservation.Court.CourtTypeId == courtTypeId.Value);
        }

        return query;
    }

    /// <summary>Active slots on candidate courts — the occupancy denominator. The start-time predicate is added by the
    /// caller.</summary>
    private IQueryable<TimeSlot> CandidateSlots(long? courtTypeId, IReadOnlyList<long> maintenanceIds)
    {
        var query = _db.TimeSlots.AsNoTracking().Where(s =>
            s.IsActive
            && s.Court.IsActive
            && !maintenanceIds.Contains(s.CourtId));

        if (courtTypeId.HasValue)
        {
            query = query.Where(s => s.Court.CourtTypeId == courtTypeId.Value);
        }

        return query;
    }

    // --- helpers ------------------------------------------------------------------------------------

    /// <summary>Resolves the analysis window: an omitted <c>ToUtc</c> ends now; an omitted <c>FromUtc</c> reaches back
    /// <see cref="DefaultWindowDays"/> days. Inputs are treated as UTC (the contract names them <c>*Utc</c>) and a
    /// reversed range is swapped so the window is always forward.</summary>
    private (DateTime FromUtc, DateTime ToUtc) NormalizeWindow(DashboardFiltersQuery filter)
    {
        var toUtc = AsUtc(filter.ToUtc) ?? _clock.UtcNow;
        var fromUtc = AsUtc(filter.FromUtc) ?? toUtc.AddDays(-DefaultWindowDays);
        return fromUtc <= toUtc ? (fromUtc, toUtc) : (toUtc, fromUtc);
    }

    private static DateTime? AsUtc(DateTime? value) =>
        value is null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);

    /// <summary>Percentage change vs the prior period. <c>null</c> when the prior value was zero but the current isn't
    /// (a growth-from-zero the UI shows as "new" rather than an infinite percentage); 0 when both are zero.</summary>
    private static double? Delta(decimal current, decimal previous)
    {
        if (previous == 0m)
        {
            return current == 0m ? 0d : null;
        }

        return (double)((current - previous) / previous) * 100d;
    }

    /// <summary>The rule-based health banner: low occupancy or a sharp revenue drop is "at risk"; strong occupancy with
    /// flat-or-growing revenue is "healthy"; everything in between is "watch".</summary>
    private static HealthCheckDto BuildHealth(decimal occupancyPct, double? revenueDeltaPercent)
    {
        var revenueDelta = revenueDeltaPercent ?? 0d;
        var occupancyText = $"{occupancyPct:0.#}% occupancy";

        if (occupancyPct < AtRiskOccupancyPct || revenueDelta <= RevenueAtRiskDeltaPct)
        {
            return new HealthCheckDto(
                DashboardHealthStatus.AtRisk,
                nameof(DashboardHealthStatus.AtRisk),
                "Needs attention",
                $"{occupancyText} and revenue is {TrendText(revenueDelta)} — review pricing, availability and recent cancellations.");
        }

        if (occupancyPct >= HealthyOccupancyPct && revenueDelta >= RevenueHealthyMinDeltaPct)
        {
            return new HealthCheckDto(
                DashboardHealthStatus.Healthy,
                nameof(DashboardHealthStatus.Healthy),
                "Business is healthy",
                $"{occupancyText} and revenue is {TrendText(revenueDelta)} versus the previous period.");
        }

        return new HealthCheckDto(
            DashboardHealthStatus.Watch,
            nameof(DashboardHealthStatus.Watch),
            "Worth keeping an eye on",
            $"{occupancyText} and revenue is {TrendText(revenueDelta)} — momentum is soft but not critical.");
    }

    private static string TrendText(double deltaPercent) => deltaPercent switch
    {
        > 0.5d => $"up {deltaPercent:0.#}%",
        < -0.5d => $"down {Math.Abs(deltaPercent):0.#}%",
        _ => "flat",
    };

    private readonly record struct WindowTotals(
        int Reservations, decimal Revenue, decimal OccupancyPct, int ActiveUsers, int BookedSlots);
}
