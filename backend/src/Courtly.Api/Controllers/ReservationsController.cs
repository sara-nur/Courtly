using Courtly.Application.Reservations;
using Courtly.Contracts.Common;
using Courtly.Contracts.Errors;
using Courtly.Contracts.Reservations;
using Courtly.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Courtly.Api.Controllers;

/// <summary>
/// The reservation engine API (feature 14). A thin controller over <see cref="IReservationService"/>: it model-binds,
/// calls the service, and returns the DTO — the state machine, preconditions and pricing live in the service, never
/// here (rubric §7). Every endpoint requires authentication; the owner is taken from the JWT (never the route/body).
/// Booking and viewing/cancelling one's own reservation are open to any authenticated user (ownership is enforced in
/// the service); confirm/complete and the cross-user list are <b>Admin/Staff</b> only. The service throws the app's
/// custom exceptions and the exception middleware maps them to a standardized <see cref="ErrorResponse"/>.
/// </summary>
[ApiController]
[Route("api/reservations")]
[Authorize]
[Produces("application/json")]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
public sealed class ReservationsController : ControllerBase
{
    // Constant role list for [Authorize] (constant string concatenation → valid attribute argument).
    private const string AdminOrStaff = Roles.Admin + "," + Roles.Staff;

    private readonly IReservationService _reservations;

    public ReservationsController(IReservationService reservations)
    {
        _reservations = reservations;
    }

    /// <summary>Books a slot → a Pending reservation owned by the caller (server-owned price + overlap/maintenance
    /// checks).</summary>
    [HttpPost]
    public async Task<ActionResult<ReservationDetailDto>> Create(CreateReservationRequest request, CancellationToken ct)
        => Ok(await _reservations.CreateAsync(request, ct));

    /// <summary>The caller's own reservations (filtered + paginated).</summary>
    [HttpGet("mine")]
    public async Task<ActionResult<PagedResult<ReservationDto>>> Mine(
        [FromQuery] PaginationQuery pagination, [FromQuery] ReservationListQuery filter, CancellationToken ct)
        => Ok(await _reservations.ListMineAsync(filter, pagination, ct));

    /// <summary>The admin/staff reservation list across all users (filter by status/court/user/date range).</summary>
    [Authorize(Roles = AdminOrStaff)]
    [HttpGet]
    public async Task<ActionResult<PagedResult<ReservationDto>>> List(
        [FromQuery] PaginationQuery pagination, [FromQuery] ReservationListQuery filter, CancellationToken ct)
        => Ok(await _reservations.ListAsync(filter, pagination, ct));

    /// <summary>One reservation's detail (reservation + audit trail + payment). Owner or admin/staff only.</summary>
    [HttpGet("{id:long}")]
    public async Task<ActionResult<ReservationDetailDto>> GetById(long id, CancellationToken ct)
        => Ok(await _reservations.GetByIdAsync(id, ct));

    /// <summary>Pending → Confirmed (manual admin/staff confirmation).</summary>
    [Authorize(Roles = AdminOrStaff)]
    [HttpPost("{id:long}/confirm")]
    public async Task<ActionResult<ReservationDetailDto>> Confirm(long id, CancellationToken ct)
        => Ok(await _reservations.ConfirmAsync(id, ct));

    /// <summary>Pending/Confirmed → Cancelled with a required reason (owner or admin/staff; paid bookings need the
    /// refund flow in feature 16).</summary>
    [HttpPost("{id:long}/cancel")]
    public async Task<ActionResult<ReservationDetailDto>> Cancel(
        long id, CancelReservationRequest request, CancellationToken ct)
        => Ok(await _reservations.CancelAsync(id, request, ct));

    /// <summary>Confirmed → Completed, once the slot has ended (admin/staff).</summary>
    [Authorize(Roles = AdminOrStaff)]
    [HttpPost("{id:long}/complete")]
    public async Task<ActionResult<ReservationDetailDto>> Complete(long id, CancellationToken ct)
        => Ok(await _reservations.CompleteAsync(id, ct));
}
