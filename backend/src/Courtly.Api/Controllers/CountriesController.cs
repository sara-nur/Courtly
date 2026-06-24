using Courtly.Application.Abstractions;
using Courtly.Contracts.Common;
using Courtly.Contracts.Errors;
using Courtly.Contracts.Reference;
using Courtly.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Courtly.Api.Controllers;

/// <summary>
/// Reference-data CRUD for countries (feature 9). The controller is thin: it model-binds, calls
/// <see cref="ICountryService"/>, and returns the DTO — no business logic or DbContext here. Reads are open to
/// any authenticated user (Admin + Staff); writes are Admin-only. The service throws the app's custom
/// exceptions and the exception middleware maps them to a standardized <see cref="ErrorResponse"/>.
/// This is the golden controller the other four reference entities mirror.
/// </summary>
[ApiController]
[Route("api/countries")]
[Authorize]
[Produces("application/json")]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
public sealed class CountriesController : ControllerBase
{
    private readonly ICountryService _countries;

    public CountriesController(ICountryService countries)
    {
        _countries = countries;
    }

    /// <summary>One page of countries, newest-first, optionally filtered by a case-insensitive name search.</summary>
    [HttpGet("")]
    public async Task<ActionResult<PagedResult<CountryDto>>> GetPaged(
        [FromQuery] PaginationQuery pagination, [FromQuery] string? search, CancellationToken ct)
        => Ok(await _countries.GetPagedAsync(pagination, search, ct));

    /// <summary>Full name-ordered list for dropdowns (cached).</summary>
    [HttpGet("lookup")]
    public async Task<ActionResult<IReadOnlyList<CountryDto>>> GetLookup(CancellationToken ct)
        => Ok(await _countries.GetLookupAsync(ct));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<CountryDto>> GetById(long id, CancellationToken ct)
        => Ok(await _countries.GetByIdAsync(id, ct));

    [Authorize(Roles = Roles.Admin)]
    [HttpPost("")]
    public async Task<ActionResult<CountryDto>> Create(CreateCountryRequest request, CancellationToken ct)
    {
        var dto = await _countries.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [Authorize(Roles = Roles.Admin)]
    [HttpPut("{id:long}")]
    public async Task<ActionResult<CountryDto>> Update(long id, UpdateCountryRequest request, CancellationToken ct)
        => Ok(await _countries.UpdateAsync(id, request, ct));

    [Authorize(Roles = Roles.Admin)]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await _countries.DeleteAsync(id, ct);
        return NoContent();
    }
}
