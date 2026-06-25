using Courtly.Application.Courts.Media;
using Courtly.Contracts.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Courtly.Api.Controllers;

/// <summary>
/// Public image serving (feature 11). Court images are stored as <c>bytea</c> and streamed here by id so the
/// list/detail payloads carry only a relative URL, never base64 bytes. PUBLIC ([AllowAnonymous]) — the Flutter app
/// loads images without attaching a JWT, and image ids are opaque/non-sensitive. The controller is thin: it asks
/// <see cref="ICourtMediaService"/> for the raw bytes and returns them with the stored content-type, or 404.
/// </summary>
[ApiController]
[Route("api/images")]
[AllowAnonymous]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
public sealed class ImagesController : ControllerBase
{
    private readonly ICourtMediaService _media;

    public ImagesController(ICourtMediaService media)
    {
        _media = media;
    }

    /// <summary>Streams the raw image bytes with their content-type; 404 when the id does not exist.</summary>
    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    {
        var content = await _media.GetImageContentAsync(id, ct);
        if (content is null)
        {
            return NotFound();
        }

        // The bytes are user-uploaded, so stop browsers MIME-sniffing them away from the stored (validated) type.
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        // Image bytes are immutable per id — let clients cache aggressively.
        Response.Headers.CacheControl = "public, max-age=604800, immutable";

        return File(content.Value.Bytes, content.Value.ContentType);
    }
}
