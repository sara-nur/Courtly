namespace Courtly.Contracts.Dashboard;

/// <summary>
/// Dashboard analytics contracts (feature 19). One composite <see cref="DashboardMetricsDto"/> carries every KPI, chart
/// series and the derived health banner for the admin overview (PRD p.5), so the client makes a single request and the
/// server computes every aggregate with a single <c>GroupBy</c> at the database. Courts under maintenance are excluded
/// from every figure (reusing the feature-12 exclusion set). DTOs only — never raw entities, never raw ids on the wire
/// (popular courts ride by name + image url).
/// </summary>
/// <remarks>
/// All money values are in the catalog currency UNITS (not cents): the server divides the actually-charged
/// <c>AmountChargedCents</c> by 100 once, so the client never reasons about cents. Revenue is NET of refunds — because
/// a refund flips the payment to <c>Refunded</c>, summing only <c>Succeeded</c> payments already excludes refunded
/// money (subtracting refund rows on top would double-count). Every KPI carries the prior equal-length period's value
/// so the UI can render the ▲/▼% delta.
/// </remarks>
public sealed record DashboardMetricsDto(
    DateTime FromUtc,
    DateTime ToUtc,
    KpiDto TotalReservations,
    KpiDto Revenue,
    KpiDto OccupancyRate,
    KpiDto ActiveUsers,
    IReadOnlyList<RevenueTrendPointDto> RevenueTrend,
    IReadOnlyList<PopularCourtDto> PopularCourts,
    IReadOnlyList<PeakHourPointDto> PeakHours,
    HealthCheckDto Health);

/// <summary>A single KPI with its value for the selected window (<see cref="Current"/>), the same metric over the prior
/// equal-length window (<see cref="Previous"/>), and the percentage change. <see cref="DeltaPercent"/> is <c>null</c>
/// when the prior value was zero and the current is non-zero (a growth-from-zero the UI shows as "new" rather than a
/// misleading percentage).</summary>
public sealed record KpiDto(decimal Current, decimal Previous, double? DeltaPercent);

/// <summary>One point on the Revenue Trends line chart — net revenue (currency units) collected on
/// <see cref="DateUtc"/> (a UTC day, time component at midnight).</summary>
public sealed record RevenueTrendPointDto(DateTime DateUtc, decimal Amount);

/// <summary>One bar on the Most Popular Courts chart: a court by name + image (never its id), how many counted
/// reservations it took in the window, and that as a percentage of the window's counted reservations.</summary>
public sealed record PopularCourtDto(string CourtName, string? ImageUrl, int Count, double Percentage);

/// <summary>One point on the Peak Hours line chart — how many counted reservations start in hour <see cref="Hour"/>
/// (0–23, UTC). The series always carries all 24 hours (zero-filled) so the chart has no gaps.</summary>
public sealed record PeakHourPointDto(int Hour, int Count);

/// <summary>The "Business Health Check" banner — a server-derived status (from occupancy + the revenue trend vs the
/// prior period) plus a short human headline and detail. Rule-based; the thresholds live as constants in the
/// service.</summary>
public sealed record HealthCheckDto(DashboardHealthStatus Status, string StatusName, string Headline, string Detail);

/// <summary>Overall business-health signal shown in the dashboard banner.</summary>
public enum DashboardHealthStatus
{
    /// <summary>Healthy utilisation and flat-or-growing revenue.</summary>
    Healthy = 0,

    /// <summary>Soft signal — middling occupancy or a small revenue dip; worth watching.</summary>
    Watch = 1,

    /// <summary>Low occupancy or a sharp revenue drop; needs attention.</summary>
    AtRisk = 2,
}

/// <summary>Query-string filters for <c>GET /api/dashboard/metrics</c> (bound via <c>[FromQuery]</c>, applied at the
/// database). All optional: an omitted date range defaults to the last 30 days; an omitted
/// <see cref="CourtTypeId"/> spans every court type. The range is <see cref="FromUtc"/> inclusive,
/// <see cref="ToUtc"/> exclusive.</summary>
public sealed record DashboardFiltersQuery(
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    long? CourtTypeId = null);
