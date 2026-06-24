using Courtly.Application.Abstractions;
using Courtly.Contracts.Common;
using Courtly.Contracts.Errors;
using Courtly.Contracts.Reference;
using Courtly.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Courtly.Api.Controllers;

/// <summary>
/// Reference-data CRUD for amenities (feature 9). The controller is thin: it model-binds, calls
/// <see cref="IAmenityService"/>, and returns the DTO — no business logic or DbContext here. Reads are open to
/// any authenticated user (Admin + Staff); writes are Admin-only. The service throws the app's custom
/// exceptions and the exception middleware maps them to a standardized <see cref="ErrorResponse"/>.
/// Mirrors the golden <c>CountriesController</c>; amenities have no lookup endpoint.
/// </summary>
[ApiController]
[Route("api/amenities")]
[Authorize]
[Produces("application/json")]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
public sealed class AmenitiesController : ControllerBase
{
    private readonly IAmenityService _amenities;

    public AmenitiesController(IAmenityService amenities)
    {
        _amenities = amenities;
    }

    /// <summary>One page of amenities, newest-first, optionally filtered by a case-insensitive name search.</summary>
    [HttpGet("")]
    public async Task<ActionResult<PagedResult<AmenityDto>>> GetPaged(
        [FromQuery] PaginationQuery pagination, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _amenities.GetPagedAsync(pagination, search, ct));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<AmenityDto>> GetById(long id, CancellationToken ct)
        => Ok(await _amenities.GetByIdAsync(id, ct));

    [Authorize(Roles = Roles.Admin)]
    [HttpPost("")]
    public async Task<ActionResult<AmenityDto>> Create(CreateAmenityRequest request, CancellationToken ct)
    {
        var dto = await _amenities.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [Authorize(Roles = Roles.Admin)]
    [HttpPut("{id:long}")]
    public async Task<ActionResult<AmenityDto>> Update(long id, UpdateAmenityRequest request, CancellationToken ct)
        => Ok(await _amenities.UpdateAsync(id, request, ct));

    [Authorize(Roles = Roles.Admin)]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await _amenities.DeleteAsync(id, ct);
        return NoContent();
    }
}
