using Courtly.Application.Abstractions;
using Courtly.Contracts.Common;
using Courtly.Contracts.Court;
using Courtly.Contracts.Errors;
using Courtly.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Courtly.Api.Controllers;

/// <summary>
/// Court-catalog CRUD (feature 10). The controller is thin: it model-binds, calls <see cref="ICourtService"/>,
/// and returns the DTO — no business logic or DbContext here. Reads are open to any authenticated user
/// (Admin + Staff); writes are Admin-only. The service throws the app's custom exceptions and the exception
/// middleware maps them to a standardized <see cref="ErrorResponse"/>. Mirrors the feature 9
/// <c>SurfaceTypesController</c> with a richer filter set on the list endpoint.
/// </summary>
[ApiController]
[Route("api/courts")]
[Authorize]
[Produces("application/json")]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
public sealed class CourtsController : ControllerBase
{
    private readonly ICourtService _courts;

    public CourtsController(ICourtService courts)
    {
        _courts = courts;
    }

    /// <summary>One page of courts, newest-first, with every filter applied at the database.</summary>
    [HttpGet("")]
    public async Task<ActionResult<PagedResult<CourtDto>>> GetPaged(
        [FromQuery] PaginationQuery pagination, [FromQuery] CourtListQuery filter, CancellationToken ct)
        => Ok(await _courts.GetPagedAsync(pagination, filter, ct));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<CourtDto>> GetById(long id, CancellationToken ct)
        => Ok(await _courts.GetByIdAsync(id, ct));

    [Authorize(Roles = Roles.Admin)]
    [HttpPost("")]
    public async Task<ActionResult<CourtDto>> Create(CreateCourtRequest request, CancellationToken ct)
    {
        var dto = await _courts.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [Authorize(Roles = Roles.Admin)]
    [HttpPut("{id:long}")]
    public async Task<ActionResult<CourtDto>> Update(long id, UpdateCourtRequest request, CancellationToken ct)
        => Ok(await _courts.UpdateAsync(id, request, ct));

    [Authorize(Roles = Roles.Admin)]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await _courts.DeleteAsync(id, ct);
        return NoContent();
    }
}
