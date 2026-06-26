using Courtly.Contracts.Common;
using Courtly.Contracts.Reservations;

namespace Courtly.Application.Reservations;

/// <summary>
/// The reservation engine (feature 14): create a booking and drive it through the centralized state machine, writing
/// an audit row and publishing an event on every transition. The transition methods return the full
/// <see cref="ReservationDetailDto"/> (reservation + audit trail + payment) so a caller sees the new state immediately.
/// </summary>
public interface IReservationService
{
    /// <summary>Books a slot → a <c>Pending</c> reservation owned by the caller (server-owned price + hold expiry,
    /// slot/court/maintenance/overlap preconditions checked server-side).</summary>
    Task<ReservationDetailDto> CreateAsync(CreateReservationRequest request, CancellationToken ct = default);

    /// <summary>Pending → Confirmed (admin/staff manual confirmation; the payment webhook reuses the same path in
    /// feature 16).</summary>
    Task<ReservationDetailDto> ConfirmAsync(long id, CancellationToken ct = default);

    /// <summary>Pending/Confirmed → Cancelled with a required reason. The owner may cancel their own; admin/staff may
    /// cancel anyone's. A paid booking is blocked here (cancelling it needs the refund flow, feature 16).</summary>
    Task<ReservationDetailDto> CancelAsync(long id, CancelReservationRequest request, CancellationToken ct = default);

    /// <summary>Confirmed → Completed, allowed only once the slot has ended (admin/staff; auto-completion via the
    /// Worker is feature 17).</summary>
    Task<ReservationDetailDto> CompleteAsync(long id, CancellationToken ct = default);

    /// <summary>One reservation's detail (reservation + audit trail + payment). Owner or admin/staff only.</summary>
    Task<ReservationDetailDto> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>The caller's own reservations (owner forced from the JWT), filtered + paginated, newest-first.</summary>
    Task<PagedResult<ReservationDto>> ListMineAsync(
        ReservationListQuery filter, PaginationQuery pagination, CancellationToken ct = default);

    /// <summary>The admin/staff reservation list across all users, filtered + paginated, newest-first.</summary>
    Task<PagedResult<ReservationDto>> ListAsync(
        ReservationListQuery filter, PaginationQuery pagination, CancellationToken ct = default);
}
