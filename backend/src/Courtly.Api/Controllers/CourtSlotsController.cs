using Courtly.Application.Abstractions;
using Courtly.Contracts.Court;
using Courtly.Contracts.Errors;
using Courtly.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Courtly.Api.Controllers;

/// <summary>
/// Time slots &amp; availability (feature 13). A thin controller over <see cref="ITimeSlotService"/>: it model-binds,
/// calls the service, and returns the DTO — the generation/pricing/bucketing logic lives in the service, never here.
/// Reading a day's <b>availability</b> is open to any authenticated user (the admin app today; the mobile booking
/// flow reuses it later); <b>generating</b> and <b>removing</b> slots are Admin-only. The service throws the app's
/// custom exceptions and the exception middleware maps them to a standardized <see cref="ErrorResponse"/>. Mirrors the
/// F12 <c>CourtMaintenanceController</c> (routes nested under <c>/api/courts/{courtId}</c>).
/// </summary>
[ApiController]
[Route("api/courts/{courtId:long}/slots")]
[Authorize]
[Produces("application/json")]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
public sealed class CourtSlotsController : ControllerBase
{
    private readonly ITimeSlotService _slots;

    public CourtSlotsController(ITimeSlotService slots)
    {
        _slots = slots;
    }

    /// <summary>One day's availability for a court: active slots grouped Morning/Afternoon/Evening with free/taken.</summary>
    [HttpGet("availability")]
    public async Task<ActionResult<DayAvailabilityDto>> GetAvailability(
        long courtId, [FromQuery] DateOnly date, CancellationToken ct)
        => Ok(await _slots.GetDayAvailabilityAsync(courtId, date, ct));

    /// <summary>Generates the court's bookable slots over a date range and a daily open/close window (server-owned
    /// price + bucket; existing starts are skipped).</summary>
    [Authorize(Roles = Roles.Admin)]
    [HttpPost("generate")]
    public async Task<ActionResult<GenerateSlotsResult>> Generate(
        long courtId, GenerateSlotsRequest request, CancellationToken ct)
        => Ok(await _slots.GenerateAsync(courtId, request, ct));

    /// <summary>Removes one slot (blocked when it has an active booking).</summary>
    [Authorize(Roles = Roles.Admin)]
    [HttpDelete("{slotId:long}")]
    public async Task<IActionResult> RemoveSlot(long courtId, long slotId, CancellationToken ct)
    {
        await _slots.RemoveSlotAsync(courtId, slotId, ct);
        return NoContent();
    }

    /// <summary>Removes a whole day's removable slots (keeping any that are actively booked) and reports the counts.</summary>
    [Authorize(Roles = Roles.Admin)]
    [HttpDelete("")]
    public async Task<ActionResult<RemoveSlotsResult>> RemoveDay(
        long courtId, [FromQuery] DateOnly date, CancellationToken ct)
        => Ok(await _slots.RemoveDayAsync(courtId, date, ct));
}
