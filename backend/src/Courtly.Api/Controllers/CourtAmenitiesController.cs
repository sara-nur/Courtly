using Courtly.Application.Courts.Media;
using Courtly.Contracts.Court;
using Courtly.Contracts.Errors;
using Courtly.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Courtly.Api.Controllers;

/// <summary>
/// Court amenities sub-resource (feature 11). Read is open to any authenticated user; the write REPLACES the
/// court's whole amenity set (Admin-only). The controller is thin: it model-binds and calls
/// <see cref="ICourtMediaService"/>, which validates every referenced amenity exists (clean 404) and swaps the
/// rows wholesale. The middleware maps the service's exceptions to a standardized <see cref="ErrorResponse"/>.
/// </summary>
[ApiController]
[Route("api/courts/{courtId:long}/amenities")]
[Authorize]
[Produces("application/json")]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
public sealed class CourtAmenitiesController : ControllerBase
{
    private readonly ICourtMediaService _media;

    public CourtAmenitiesController(ICourtMediaService media)
    {
        _media = media;
    }

    /// <summary>A court's amenities with resolved name/icon and per-court payload.</summary>
    [HttpGet("")]
    public async Task<ActionResult<IReadOnlyList<CourtAmenityDto>>> GetAmenities(long courtId, CancellationToken ct)
        => Ok(await _media.GetAmenitiesAsync(courtId, ct));

    /// <summary>Replaces the court's whole amenity set with the request set.</summary>
    [Authorize(Roles = Roles.Admin)]
    [HttpPut("")]
    public async Task<ActionResult<IReadOnlyList<CourtAmenityDto>>> SetAmenities(
        long courtId, SetCourtAmenitiesRequest request, CancellationToken ct)
        => Ok(await _media.SetAmenitiesAsync(courtId, request, ct));
}
