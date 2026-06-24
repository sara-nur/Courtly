using Courtly.Application.Abstractions;
using Courtly.Contracts.Common;
using Courtly.Contracts.Errors;
using Courtly.Contracts.Reference;
using Courtly.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Courtly.Api.Controllers;

/// <summary>
/// Reference-data CRUD for court types (feature 9). The controller is thin: it model-binds, calls
/// <see cref="ICourtTypeService"/>, and returns the DTO — no business logic or DbContext here. Reads are open to
/// any authenticated user (Admin + Staff); writes are Admin-only. The service throws the app's custom exceptions
/// and the exception middleware maps them to a standardized <see cref="ErrorResponse"/>. Mirrors the golden
/// CountriesController (no lookup endpoint — that is Country-only).
/// </summary>
[ApiController]
[Route("api/court-types")]
[Authorize]
[Produces("application/json")]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
public sealed class CourtTypesController : ControllerBase
{
    private readonly ICourtTypeService _courtTypes;

    public CourtTypesController(ICourtTypeService courtTypes)
    {
        _courtTypes = courtTypes;
    }

    /// <summary>One page of court types, newest-first, optionally filtered by a case-insensitive name search.</summary>
    [HttpGet("")]
    public async Task<ActionResult<PagedResult<CourtTypeDto>>> GetPaged(
        [FromQuery] PaginationQuery pagination, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _courtTypes.GetPagedAsync(pagination, search, ct));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<CourtTypeDto>> GetById(long id, CancellationToken ct)
        => Ok(await _courtTypes.GetByIdAsync(id, ct));

    [Authorize(Roles = Roles.Admin)]
    [HttpPost("")]
    public async Task<ActionResult<CourtTypeDto>> Create(CreateCourtTypeRequest request, CancellationToken ct)
    {
        var dto = await _courtTypes.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [Authorize(Roles = Roles.Admin)]
    [HttpPut("{id:long}")]
    public async Task<ActionResult<CourtTypeDto>> Update(long id, UpdateCourtTypeRequest request, CancellationToken ct)
        => Ok(await _courtTypes.UpdateAsync(id, request, ct));

    [Authorize(Roles = Roles.Admin)]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await _courtTypes.DeleteAsync(id, ct);
        return NoContent();
    }
}
