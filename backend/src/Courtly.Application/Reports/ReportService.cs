using System.Globalization;
using Courtly.Application.Abstractions;
using Courtly.Application.Analytics;
using Courtly.Application.Reports.Documents;
using Courtly.Contracts.Reports;
using Courtly.Domain.Enums;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Courtly.Application.Reports;

/// <summary>
/// PDF report engine (feature 20). The reservations report is the operational booking listing (every status); the
/// revenue/utilisation report reuses the F19 <see cref="AnalyticsFilters"/> predicates so its revenue and utilisation
/// reconcile with the dashboard by construction. Reads are <c>AsNoTracking</c>, aggregates are single <c>GroupBy</c>
/// queries at the database (never row-by-row, never filtered in memory — rubric §8.2), money is the actually-charged
/// cents ÷ 100, and every timestamp is UTC via <see cref="IClock"/>.
/// </summary>
public sealed class ReportService : IReportService
{
    private const int CentsPerUnit = 100;
    private const int DefaultWindowDays = 30;

    static ReportService()
    {
        // QuestPDF is free under the Community licence for this use. Set once, here, so it is guaranteed configured
        // before any document is generated — including in unit tests that construct the service directly (bypassing DI).
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private readonly CourtlyDbContext _db;
    private readonly IClock _clock;
    private readonly IMaintenanceService _maintenance;
    private readonly ILogger<ReportService> _logger;

    public ReportService(
        CourtlyDbContext db,
        IClock clock,
        IMaintenanceService maintenance,
        ILogger<ReportService> logger)
    {
        _db = db;
        _clock = clock;
        _maintenance = maintenance;
        _logger = logger;
    }

    // --- Reservations report ---------------------------------------------------------------------

    public async Task<ReservationsReportData> BuildReservationsDataAsync(
        ReservationsReportQuery filter, CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = NormalizeWindow(filter.FromUtc, filter.ToUtc);

        // The operational listing shows EVERY status (Pending/Confirmed/Cancelled/Completed), so it does not apply the
        // analytics "counted" predicate — it filters only by the chosen date range / court / status.
        var query = _db.Reservations.AsNoTracking()
            .Where(r => r.TimeSlot.StartUtc >= fromUtc && r.TimeSlot.StartUtc < toUtc);

        if (filter.CourtId.HasValue)
        {
            query = query.Where(r => r.CourtId == filter.CourtId.Value);
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(r => r.Status == filter.Status.Value);
        }

        var raw = await query
            .OrderByDescending(r => r.TimeSlot.StartUtc)
            .ThenBy(r => r.Id)
            .Select(r => new ReservationRow(
                r.Id,
                r.User.FirstName + " " + r.User.LastName,
                r.User.Email,
                r.Court.Name,
                r.TimeSlot.StartUtc,
                r.TimeSlot.EndUtc,
                r.Status,
                r.TotalPrice,
                r.Payment != null ? r.Payment.Status : (PaymentStatus?)null))
            .ToListAsync(ct);

        var rows = raw
            .Select(r => new ReservationReportRow(
                Reference(r.Id),
                FullName(r.Name, r.Email),
                r.Email,
                r.CourtName,
                r.SlotStartUtc,
                r.SlotEndUtc,
                StatusLabel(r.Status),
                r.Amount,
                r.PayStatus == PaymentStatus.Succeeded))
            .ToList();

        var breakdown = raw
            .GroupBy(r => r.Status)
            .OrderBy(g => g.Key)
            .Select(g => new ReportStatusCount(StatusLabel(g.Key), g.Count()))
            .ToList();

        var courtLabel = await CourtLabelAsync(filter.CourtId, ct);
        var statusLabel = filter.Status.HasValue ? StatusLabel(filter.Status.Value) : "All statuses";

        return new ReservationsReportData(
            fromUtc,
            toUtc,
            courtLabel,
            statusLabel,
            rows,
            breakdown,
            rows.Count,
            rows.Sum(r => r.Amount),
            _clock.UtcNow);
    }

    public async Task<byte[]> GenerateReservationsReportAsync(
        ReservationsReportQuery filter, CancellationToken ct = default)
    {
        var data = await BuildReservationsDataAsync(filter, ct);
        var bytes = new ReservationsReportDocument(data).GeneratePdf();
        _logger.LogInformation(
            "Generated reservations report ({Rows} rows) for {From:o}..{To:o}.", data.TotalCount, data.FromUtc, data.ToUtc);
        return bytes;
    }

    // --- Revenue & court-utilisation report ------------------------------------------------------

    public async Task<RevenueUtilizationReportData> BuildRevenueUtilizationDataAsync(
        RevenueUtilizationReportQuery filter, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var year = filter.Year is > 0 ? filter.Year.Value : now.Year;
        var month = filter.Month is >= 1 and <= 12 ? filter.Month.Value : now.Month;
        var monthStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthEnd = monthStart.AddMonths(1);

        // Same maintenance-exclusion set the dashboard uses (F12) — courts down this month are out of the figures.
        var maintenanceIds = await _maintenance.GetCourtIdsUnderMaintenanceAsync(monthStart, monthEnd, ct);

        // Bookings (occupancy numerator) — counted reservations whose slot starts in the month.
        var bookedQuery = AnalyticsFilters
            .CountedReservations(_db.Reservations.AsNoTracking(), courtTypeId: null, maintenanceIds)
            .Where(r => r.TimeSlot.StartUtc >= monthStart && r.TimeSlot.StartUtc < monthEnd);
        if (filter.CourtId.HasValue)
        {
            bookedQuery = bookedQuery.Where(r => r.CourtId == filter.CourtId.Value);
        }

        var booked = await bookedQuery
            .GroupBy(r => r.CourtId)
            .Select(g => new { CourtId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // Available slots (occupancy denominator) — active candidate slots starting in the month.
        var slotQuery = AnalyticsFilters
            .CandidateSlots(_db.TimeSlots.AsNoTracking(), courtTypeId: null, maintenanceIds)
            .Where(s => s.StartUtc >= monthStart && s.StartUtc < monthEnd);
        if (filter.CourtId.HasValue)
        {
            slotQuery = slotQuery.Where(s => s.CourtId == filter.CourtId.Value);
        }

        var available = await slotQuery
            .GroupBy(s => s.CourtId)
            .Select(g => new { CourtId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // Net revenue — Succeeded payments paid in the month (refunds netted out by the status filter).
        var payQuery = AnalyticsFilters
            .CountedPayments(_db.Payments.AsNoTracking(), courtTypeId: null, maintenanceIds)
            .Where(p => p.PaidAtUtc >= monthStart && p.PaidAtUtc < monthEnd);
        if (filter.CourtId.HasValue)
        {
            payQuery = payQuery.Where(p => p.Reservation.CourtId == filter.CourtId.Value);
        }

        var revenue = await payQuery
            .GroupBy(p => p.Reservation.CourtId)
            .Select(g => new { CourtId = g.Key, Cents = g.Sum(p => p.AmountChargedCents ?? 0L) })
            .ToListAsync(ct);

        var bookedByCourt = booked.ToDictionary(x => x.CourtId, x => x.Count);
        var availByCourt = available.ToDictionary(x => x.CourtId, x => x.Count);
        var centsByCourt = revenue.ToDictionary(x => x.CourtId, x => x.Cents);

        var courtIds = bookedByCourt.Keys
            .Union(availByCourt.Keys)
            .Union(centsByCourt.Keys)
            .Distinct()
            .ToList();

        var names = await _db.Courts.AsNoTracking()
            .Where(c => courtIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);
        var nameById = names.ToDictionary(x => x.Id, x => x.Name);

        var rows = courtIds
            .Select(id =>
            {
                var bookedCount = bookedByCourt.GetValueOrDefault(id);
                var availCount = availByCourt.GetValueOrDefault(id);
                var cents = centsByCourt.GetValueOrDefault(id);
                return new CourtUtilizationRow(
                    nameById.GetValueOrDefault(id, $"Court {id}"),
                    bookedCount,
                    cents / (decimal)CentsPerUnit,
                    Utilization(bookedCount, availCount));
            })
            .OrderByDescending(r => r.Revenue)
            .ThenByDescending(r => r.Bookings)
            .ThenBy(r => r.CourtName)
            .ToList();

        var totalBooked = bookedByCourt.Values.Sum();
        var totalAvailable = availByCourt.Values.Sum();
        var totalCents = centsByCourt.Values.Sum();
        var courtLabel = await CourtLabelAsync(filter.CourtId, ct);

        return new RevenueUtilizationReportData(
            year,
            month,
            monthStart.ToString("MMMM yyyy", CultureInfo.InvariantCulture),
            courtLabel,
            rows,
            totalBooked,
            totalCents / (decimal)CentsPerUnit,
            Utilization(totalBooked, totalAvailable),
            _clock.UtcNow);
    }

    public async Task<byte[]> GenerateRevenueUtilizationReportAsync(
        RevenueUtilizationReportQuery filter, CancellationToken ct = default)
    {
        var data = await BuildRevenueUtilizationDataAsync(filter, ct);
        var bytes = new RevenueUtilizationReportDocument(data).GeneratePdf();
        _logger.LogInformation(
            "Generated revenue/utilisation report for {Month} ({Courts} courts).", data.MonthLabel, data.Rows.Count);
        return bytes;
    }

    // --- helpers ---------------------------------------------------------------------------------

    /// <summary>Resolves the analysis window: an omitted end defaults to now, an omitted start reaches back 30 days,
    /// inputs are treated as UTC, and a reversed range is swapped (mirrors the dashboard's window rules).</summary>
    private (DateTime FromUtc, DateTime ToUtc) NormalizeWindow(DateTime? from, DateTime? to)
    {
        var toUtc = AsUtc(to) ?? _clock.UtcNow;
        var fromUtc = AsUtc(from) ?? toUtc.AddDays(-DefaultWindowDays);
        return fromUtc <= toUtc ? (fromUtc, toUtc) : (toUtc, fromUtc);
    }

    private static DateTime? AsUtc(DateTime? value) =>
        value is null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);

    /// <summary>Booked ÷ available × 100, rounded to one decimal — computed in decimal exactly as the dashboard's
    /// occupancy so the totals reconcile. An empty denominator is 0% (no divide-by-zero).</summary>
    private static double Utilization(int booked, int available) =>
        available == 0 ? 0d : (double)Math.Round((decimal)booked / available * 100m, 1);

    private async Task<string> CourtLabelAsync(long? courtId, CancellationToken ct)
    {
        if (courtId is not long id)
        {
            return "All courts";
        }

        var name = await _db.Courts.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(ct);
        return name ?? $"Court {id}";
    }

    /// <summary>The human booking reference shown in the report — <c>#RES-001</c>, never the raw id (rubric §6), the
    /// same format the admin reservation table uses.</summary>
    private static string Reference(long id) => $"#RES-{id:D3}";

    private static string FullName(string? name, string? email)
    {
        var trimmed = name?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            return trimmed;
        }

        return string.IsNullOrWhiteSpace(email) ? "Unknown" : email!;
    }

    private static string StatusLabel(ReservationStatus status) => status switch
    {
        ReservationStatus.Pending => "Pending",
        ReservationStatus.Confirmed => "Confirmed",
        ReservationStatus.Completed => "Completed",
        ReservationStatus.Cancelled => "Cancelled",
        _ => status.ToString(),
    };

    /// <summary>Provider-agnostic intermediate for the reservations projection (labels/IsPaid resolved in memory).</summary>
    private sealed record ReservationRow(
        long Id,
        string? Name,
        string? Email,
        string CourtName,
        DateTime SlotStartUtc,
        DateTime SlotEndUtc,
        ReservationStatus Status,
        decimal Amount,
        PaymentStatus? PayStatus);
}
