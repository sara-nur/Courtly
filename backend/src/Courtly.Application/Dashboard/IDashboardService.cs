using Courtly.Contracts.Dashboard;

namespace Courtly.Application.Dashboard;

/// <summary>
/// Dashboard analytics (feature 19). Computes the admin overview's KPIs, chart series and health banner from the
/// reservation (F14) and payment (F16) data, excluding courts under maintenance (F12). Every aggregate is a single
/// <c>GroupBy</c> at the database (rubric §8.2 — no per-row queries, no in-memory filtering), and the composite result
/// is cached for a short TTL so the client's auto-refresh never recomputes the heavy queries each tick (roadmap §7).
/// </summary>
public interface IDashboardService
{
    /// <summary>The full dashboard payload for the given filters: 4 KPIs (each with its prior-period value), the
    /// revenue / popular-courts / peak-hours series, and the derived health banner. An omitted date range defaults to
    /// the last 30 days; an omitted court type spans every type.</summary>
    Task<DashboardMetricsDto> GetMetricsAsync(DashboardFiltersQuery filter, CancellationToken ct = default);
}
