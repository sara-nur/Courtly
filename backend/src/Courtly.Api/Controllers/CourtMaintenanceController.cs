using Courtly.Application.Abstractions;
using Courtly.Contracts.Common;
using Courtly.Contracts.Court;
using Courtly.Contracts.Errors;
using Courtly.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Courtly.Api.Controllers;

/// <summary>
/// Court status &amp; maintenance (feature 12). A thin controller over <see cref="IMaintenanceService"/>: it
/// model-binds, calls the service, and returns the DTO — the state-machine logic lives in the service, never here.
/// Reading the status history is open to any authenticated admin-app user (Admin + Staff); every write (open a
/// window, start, fix, cancel) is Admin-only. The service throws the app's custom exceptions and the exception
/// middleware maps them to a standardized <see cref="ErrorResponse"/>. Mirrors the F11 court sub-resource
/// controllers (routes nested under <c>/api/courts/{courtId}</c>).
/// </summary>
[ApiController]
[Route("api/courts/{courtId:long}/maintenance")]
[Authorize]
[Produces("application/json")]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
public sealed class CourtMaintenanceController : ControllerBase
{
    private readonly IMaintenanceService _maintenance;

    public CourtMaintenanceController(IMaintenanceService maintenance)
    {
        _maintenance = maintenance;
    }

    /// <summary>One page of the court's maintenance windows (status history), newest-first.</summary>
    [HttpGet("")]
    public async Task<ActionResult<PagedResult<CourtMaintenanceLogDto>>> GetHistory(
        long courtId, [FromQuery] PaginationQuery pagination, CancellationToken ct)
        => Ok(await _maintenance.GetHistoryAsync(courtId, pagination, ct));

    /// <summary>Opens a maintenance window — immediately (no future start) or scheduled (future start).</summary>
    [Authorize(Roles = Roles.Admin)]
    [HttpPost("")]
    public async Task<ActionResult<CourtMaintenanceLogDto>> Create(
        long courtId, CreateMaintenanceRequest request, CancellationToken ct)
        => Ok(await _maintenance.CreateAsync(courtId, request, ct));

    /// <summary>Starts a scheduled window now (Scheduled → InProgress).</summary>
    [Authorize(Roles = Roles.Admin)]
    [HttpPost("{logId:long}/start")]
    public async Task<ActionResult<CourtMaintenanceLogDto>> Start(long courtId, long logId, CancellationToken ct)
        => Ok(await _maintenance.StartAsync(courtId, logId, ct));

    /// <summary>The "Fix" action: completes an in-progress window and frees the court (InProgress → Completed).</summary>
    [Authorize(Roles = Roles.Admin)]
    [HttpPost("{logId:long}/fix")]
    public async Task<ActionResult<CourtMaintenanceLogDto>> Fix(long courtId, long logId, CancellationToken ct)
        => Ok(await _maintenance.CompleteAsync(courtId, logId, ct));

    /// <summary>Cancels an open window (→ Cancelled), also freeing the court.</summary>
    [Authorize(Roles = Roles.Admin)]
    [HttpPost("{logId:long}/cancel")]
    public async Task<ActionResult<CourtMaintenanceLogDto>> Cancel(long courtId, long logId, CancellationToken ct)
        => Ok(await _maintenance.CancelAsync(courtId, logId, ct));
}
