using Courtly.Application.Abstractions;
using Courtly.Contracts.Common;
using Courtly.Contracts.Errors;
using Courtly.Contracts.Reference;
using Courtly.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Courtly.Api.Controllers;

/// <summary>
/// Reference-data CRUD for cities (feature 9). The controller is thin: it model-binds, calls
/// <see cref="ICityService"/>, and returns the DTO — no business logic or DbContext here. Reads are open to
/// any authenticated user (Admin + Staff); writes are Admin-only. The service throws the app's custom
/// exceptions and the exception middleware maps them to a standardized <see cref="ErrorResponse"/>.
/// Mirrors the golden <c>CountriesController</c> without the cached lookup endpoint (Country-only).
/// </summary>
[ApiController]
[Route("api/cities")]
[Authorize]
[Produces("application/json")]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
public sealed class CitiesController : ControllerBase
{
    private readonly ICityService _cities;

    public CitiesController(ICityService cities)
    {
        _cities = cities;
    }

    /// <summary>One page of cities, newest-first, optionally filtered by a case-insensitive name search.</summary>
    [HttpGet("")]
    public async Task<ActionResult<PagedResult<CityDto>>> GetPaged(
        [FromQuery] PaginationQuery pagination, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _cities.GetPagedAsync(pagination, search, ct));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<CityDto>> GetById(long id, CancellationToken ct)
        => Ok(await _cities.GetByIdAsync(id, ct));

    [Authorize(Roles = Roles.Admin)]
    [HttpPost("")]
    public async Task<ActionResult<CityDto>> Create(CreateCityRequest request, CancellationToken ct)
    {
        var dto = await _cities.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [Authorize(Roles = Roles.Admin)]
    [HttpPut("{id:long}")]
    public async Task<ActionResult<CityDto>> Update(long id, UpdateCityRequest request, CancellationToken ct)
        => Ok(await _cities.UpdateAsync(id, request, ct));

    [Authorize(Roles = Roles.Admin)]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await _cities.DeleteAsync(id, ct);
        return NoContent();
    }
}
