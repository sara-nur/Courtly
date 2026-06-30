using Courtly.Domain.Enums;

namespace Courtly.Contracts.Reports;

/// <summary>
/// PDF report contracts (feature 20). Two server-side QuestPDF reports for the admin <b>Reports</b> screen, each
/// downloadable and printable (rubric §2.2) and each filtered by ≥1 search parameter:
/// <list type="bullet">
///   <item><b>Reservations report</b> — the operational booking listing, filtered by date range / court / status.</item>
///   <item><b>Revenue &amp; court-utilization report</b> — per-court revenue + utilisation for a month (± one court).</item>
/// </list>
/// The query records below bind from the query string (<c>[FromQuery]</c>); the *-Data records are the shapes the
/// document builders render and the tests assert. Money is in catalog currency UNITS (cents ÷ 100), and the
/// revenue/utilisation figures reuse the same <c>AnalyticsFilters</c> predicates as the F19 dashboard, so the report
/// numbers reconcile with it by construction.
/// </summary>

// --- Reservations report -------------------------------------------------------------------------

/// <summary>Filters for the reservations report. All optional: an omitted date range defaults to the last 30 days, an
/// omitted court/status is not applied. The range spans the slot start time (<see cref="FromUtc"/> inclusive,
/// <see cref="ToUtc"/> exclusive).</summary>
public sealed record ReservationsReportQuery(
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    long? CourtId = null,
    ReservationStatus? Status = null);

/// <summary>One row of the reservations report: the human booking <see cref="Reference"/> (<c>#RES-001</c>, never the
/// raw id), the customer, the court, the slot window, the status label, the price and whether it is paid.</summary>
public sealed record ReservationReportRow(
    string Reference,
    string Customer,
    string? Email,
    string CourtName,
    DateTime SlotStartUtc,
    DateTime SlotEndUtc,
    string StatusName,
    decimal Amount,
    bool IsPaid);

/// <summary>A status total for the reservations report summary (e.g. "Confirmed: 12").</summary>
public sealed record ReportStatusCount(string StatusName, int Count);

/// <summary>Everything the reservations-report document renders: the resolved filter labels, the rows (newest slot
/// first), the per-status breakdown and the grand totals, plus when it was generated (UTC).</summary>
public sealed record ReservationsReportData(
    DateTime FromUtc,
    DateTime ToUtc,
    string CourtLabel,
    string StatusLabel,
    IReadOnlyList<ReservationReportRow> Rows,
    IReadOnlyList<ReportStatusCount> StatusBreakdown,
    int TotalCount,
    decimal TotalAmount,
    DateTime GeneratedAtUtc);

// --- Revenue & court-utilization report ----------------------------------------------------------

/// <summary>Filters for the revenue/utilisation report: a month (an omitted <see cref="Year"/>/<see cref="Month"/>
/// defaults to the current month) and an optional single <see cref="CourtId"/>.</summary>
public sealed record RevenueUtilizationReportQuery(
    int? Year = null,
    int? Month = null,
    long? CourtId = null);

/// <summary>One court's line in the revenue/utilisation report: counted bookings, net revenue (currency units, refunds
/// already netted out) and utilisation (booked ÷ available active slots, %).</summary>
public sealed record CourtUtilizationRow(
    string CourtName,
    int Bookings,
    decimal Revenue,
    double UtilizationPct);

/// <summary>Everything the revenue/utilisation document renders: the month label, the resolved court filter, the
/// per-court rows (highest revenue first), the grand totals and overall utilisation, plus when it was generated
/// (UTC).</summary>
public sealed record RevenueUtilizationReportData(
    int Year,
    int Month,
    string MonthLabel,
    string CourtLabel,
    IReadOnlyList<CourtUtilizationRow> Rows,
    int TotalBookings,
    decimal TotalRevenue,
    double OverallUtilizationPct,
    DateTime GeneratedAtUtc);
