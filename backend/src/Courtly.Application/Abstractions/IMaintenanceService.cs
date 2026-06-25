using Courtly.Contracts.Common;
using Courtly.Contracts.Court;

namespace Courtly.Application.Abstractions;

/// <summary>
/// Court status &amp; maintenance (feature 12). Owns the maintenance windows of a court and the rules that move them
/// through the <see cref="Courtly.Application.Courts.Maintenance.MaintenanceStateMachine"/>, plus the read-side
/// predicates the booking (F13) and analytics (F19) features reuse to treat a court under maintenance as
/// unavailable / excluded.
/// </summary>
public interface IMaintenanceService
{
    /// <summary>One page of a court's maintenance windows, newest-first — the "who/when/why" status history.</summary>
    Task<PagedResult<CourtMaintenanceLogDto>> GetHistoryAsync(
        long courtId, PaginationQuery pagination, CancellationToken ct = default);

    /// <summary>Opens a maintenance window: immediate (<c>InProgress</c> now) when no future start is given, else a
    /// future <c>Scheduled</c> window. Rejects a window that overlaps an existing open one.</summary>
    Task<CourtMaintenanceLogDto> CreateAsync(
        long courtId, CreateMaintenanceRequest request, CancellationToken ct = default);

    /// <summary>Starts a <c>Scheduled</c> window now (Scheduled → InProgress).</summary>
    Task<CourtMaintenanceLogDto> StartAsync(long courtId, long logId, CancellationToken ct = default);

    /// <summary>The "Fix" action: completes an in-progress window and frees the court (InProgress → Completed,
    /// sets <c>EndUtc</c>).</summary>
    Task<CourtMaintenanceLogDto> CompleteAsync(long courtId, long logId, CancellationToken ct = default);

    /// <summary>Cancels an open (Scheduled/InProgress) window (→ Cancelled), also freeing the court.</summary>
    Task<CourtMaintenanceLogDto> CancelAsync(long courtId, long logId, CancellationToken ct = default);

    /// <summary>True when the court has an open window covering "now" — the unavailable-for-booking predicate.</summary>
    Task<bool> IsCourtUnderMaintenanceAsync(long courtId, CancellationToken ct = default);

    /// <summary>Ids of courts whose open maintenance windows overlap <c>[fromUtc, toUtc)</c>. Reused by booking
    /// (exclude from bookable lists) and analytics (exclude from dashboard stats).</summary>
    Task<IReadOnlyList<long>> GetCourtIdsUnderMaintenanceAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
}
