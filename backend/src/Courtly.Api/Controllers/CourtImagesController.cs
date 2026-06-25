using Courtly.Application.Common.Exceptions;
using Courtly.Application.Courts.Media;
using Courtly.Contracts.Court;
using Courtly.Contracts.Errors;
using Courtly.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Courtly.Api.Controllers;

/// <summary>
/// Court images sub-resource (feature 11). The list is PUBLIC (the Flutter app renders a court's gallery without a
/// JWT); upload / set-primary / delete are Admin-only. The controller is thin: it reads the multipart file into a
/// byte[] and delegates to <see cref="ICourtMediaService"/>, which validates the content (MIME + magic bytes +
/// size) and maintains the single-primary invariant. The middleware maps the service's exceptions to a
/// standardized <see cref="ErrorResponse"/>.
/// </summary>
[ApiController]
[Route("api/courts/{courtId:long}/images")]
[Authorize]
[Produces("application/json")]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
public sealed class CourtImagesController : ControllerBase
{
    private readonly ICourtMediaService _media;

    public CourtImagesController(ICourtMediaService media)
    {
        _media = media;
    }

    /// <summary>A court's images, primary first; PUBLIC read.</summary>
    [AllowAnonymous]
    [HttpGet("")]
    public async Task<ActionResult<IReadOnlyList<CourtImageDto>>> GetImages(long courtId, CancellationToken ct)
        => Ok(await _media.GetImagesAsync(courtId, ct));

    /// <summary>Uploads an image (multipart/form-data). If the court has no images or <c>isPrimary</c> is set, the
    /// new image becomes primary.</summary>
    [Authorize(Roles = Roles.Admin)]
    [HttpPost("")]
    [RequestSizeLimit(5_242_880)]
    [RequestFormLimits(MultipartBodyLengthLimit = 5_242_880)]
    public async Task<ActionResult<CourtImageDto>> Upload(
        long courtId,
        IFormFile file,
        [FromForm] string? caption,
        [FromForm] bool isPrimary,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            throw new ValidationException(
                "An image file is required.",
                new Dictionary<string, string[]> { ["file"] = new[] { "An image file is required." } });
        }

        // Reject oversize uploads up front (clean 400) from the declared length, BEFORE materializing the body —
        // so we never allocate megabytes only to reject them, and the client gets a friendly message instead of a
        // framework 413/500. The service re-checks the real byte length as defense-in-depth.
        if (file.Length > ImageContentValidator.MaxBytes)
        {
            throw new ValidationException(
                "Image too large.",
                new Dictionary<string, string[]>
                {
                    ["file"] = new[] { $"Image must be at most {ImageContentValidator.MaxBytes / (1024 * 1024)} MB." },
                });
        }

        // Length is now bounded, so size the buffer once to avoid MemoryStream's grow-and-copy churn.
        using var ms = new MemoryStream((int)file.Length);
        await file.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();

        var dto = await _media.AddImageAsync(courtId, bytes, file.ContentType, caption, isPrimary, ct);
        return CreatedAtAction(nameof(GetImages), new { courtId }, dto);
    }

    /// <summary>Makes the given image the court's primary, unsetting the rest.</summary>
    [Authorize(Roles = Roles.Admin)]
    [HttpPut("{imageId:long}/primary")]
    public async Task<ActionResult<CourtImageDto>> SetPrimary(long courtId, long imageId, CancellationToken ct)
        => Ok(await _media.SetPrimaryImageAsync(courtId, imageId, ct));

    /// <summary>Deletes an image; if it was primary, the earliest remaining image is promoted.</summary>
    [Authorize(Roles = Roles.Admin)]
    [HttpDelete("{imageId:long}")]
    public async Task<IActionResult> Delete(long courtId, long imageId, CancellationToken ct)
    {
        await _media.DeleteImageAsync(courtId, imageId, ct);
        return NoContent();
    }
}
