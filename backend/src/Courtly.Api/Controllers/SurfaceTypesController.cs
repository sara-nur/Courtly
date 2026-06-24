using Courtly.Application.Abstractions;
using Courtly.Contracts.Common;
using Courtly.Contracts.Errors;
using Courtly.Contracts.Reference;
using Courtly.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Courtly.Api.Controllers;

/// <summary>
/// Reference-data CRUD for surface types (feature 9). The controller is thin: it model-binds, calls
/// <see cref="ISurfaceTypeService"/>, and returns the DTO — no business logic or DbContext here. Reads are open
/// to any authenticated user (Admin + Staff); writes are Admin-only. The service throws the app's custom
/// exceptions and the exception middleware maps them to a standardized <see cref="ErrorResponse"/>. Mirrors the
/// golden <c>CountriesController</c> minus the cached dropdown lookup endpoint.
/// </summary>
[ApiController]
[Route("api/surface-types")]
[Authorize]
[Produces("application/json")]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
public sealed class SurfaceTypesController : ControllerBase
{
    private readonly ISurfaceTypeService _surfaceTypes;

    public SurfaceTypesController(ISurfaceTypeService surfaceTypes)
    {
        _surfaceTypes = surfaceTypes;
    }

    /// <summary>One page of surface types, newest-first, optionally filtered by a case-insensitive name search.</summary>
    [HttpGet("")]
    public async Task<ActionResult<PagedResult<SurfaceTypeDto>>> GetPaged(
        [FromQuery] PaginationQuery pagination, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _surfaceTypes.GetPagedAsync(pagination, search, ct));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<SurfaceTypeDto>> GetById(long id, CancellationToken ct)
        => Ok(await _surfaceTypes.GetByIdAsync(id, ct));

    [Authorize(Roles = Roles.Admin)]
    [HttpPost("")]
    public async Task<ActionResult<SurfaceTypeDto>> Create(CreateSurfaceTypeRequest request, CancellationToken ct)
    {
        var dto = await _surfaceTypes.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [Authorize(Roles = Roles.Admin)]
    [HttpPut("{id:long}")]
    public async Task<ActionResult<SurfaceTypeDto>> Update(long id, UpdateSurfaceTypeRequest request, CancellationToken ct)
        => Ok(await _surfaceTypes.UpdateAsync(id, request, ct));

    [Authorize(Roles = Roles.Admin)]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await _surfaceTypes.DeleteAsync(id, ct);
        return NoContent();
    }
}
