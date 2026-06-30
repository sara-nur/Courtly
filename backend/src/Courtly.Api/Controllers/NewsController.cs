using Courtly.Application.Abstractions;
using Courtly.Application.Common.Exceptions;
using Courtly.Application.Courts.Media;
using Courtly.Contracts.Common;
using Courtly.Contracts.Errors;
using Courtly.Contracts.News;
using Courtly.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Courtly.Api.Controllers;

/// <summary>
/// News / announcements (feature 21). The controller is thin: it model-binds, reads the multipart image into a
/// byte[], calls <see cref="INewsService"/>, and returns the DTO — no business logic or DbContext here. Reads of the
/// admin list/detail are open to any authenticated user (Admin + Staff); the published feed is also authenticated
/// (the mobile client attaches its JWT); image bytes are served PUBLICLY ([AllowAnonymous]) because the Flutter
/// image loader does not send a JWT. Create / update / delete are Admin-only, the author is taken from the JWT (never
/// the body), and the image is required on create. The service throws the app's custom exceptions and the exception
/// middleware maps them to a standardized <see cref="ErrorResponse"/>. Mirrors the feature 9 controller for the CRUD
/// shape and <see cref="CourtImagesController"/> for the multipart upload.
/// </summary>
[ApiController]
[Route("api/news")]
[Authorize]
[Produces("application/json")]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
public sealed class NewsController : ControllerBase
{
    private readonly INewsService _news;
    private readonly ICurrentUser _currentUser;

    public NewsController(INewsService news, ICurrentUser currentUser)
    {
        _news = news;
        _currentUser = currentUser;
    }

    /// <summary>One admin page of news, newest-first, optionally filtered by a case-insensitive Title/Text search and
    /// by active state (<c>activeOnly=true</c> published only, <c>false</c> hidden only, omitted = both).</summary>
    [HttpGet("")]
    public async Task<ActionResult<PagedResult<NewsDto>>> GetPaged(
        [FromQuery] PaginationQuery pagination,
        [FromQuery] string? search,
        [FromQuery] bool? activeOnly,
        CancellationToken ct)
        => Ok(await _news.GetPagedAsync(pagination, search, activeOnly, ct));

    /// <summary>One page of <em>published</em> news (active + already published), newest-first. Client feed (F23).</summary>
    [HttpGet("published")]
    public async Task<ActionResult<PagedResult<NewsDto>>> GetPublished(
        [FromQuery] PaginationQuery pagination, CancellationToken ct)
        => Ok(await _news.GetPublishedAsync(pagination, ct));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<NewsDto>> GetById(long id, CancellationToken ct)
        => Ok(await _news.GetByIdAsync(id, ct));

    /// <summary>Streams the article's image bytes with their stored content-type; 404 when the id does not exist.
    /// PUBLIC — the Flutter image loader does not attach a JWT and ids are opaque/non-sensitive.</summary>
    [AllowAnonymous]
    [HttpGet("{id:long}/image")]
    public async Task<IActionResult> GetImage(long id, CancellationToken ct)
    {
        var content = await _news.GetImageContentAsync(id, ct);
        if (content is null)
        {
            return NotFound();
        }

        // Stop browsers MIME-sniffing user-uploaded bytes away from the stored (validated) type.
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        // A news image CAN be replaced in place on edit (same id, new bytes), so — unlike immutable court images —
        // allow only a short cache window and force revalidation, never "immutable".
        Response.Headers.CacheControl = "public, max-age=300, must-revalidate";

        return File(content.Value.Bytes, content.Value.ContentType);
    }

    [Authorize(Roles = Roles.Admin)]
    [HttpPost("")]
    [RequestSizeLimit(ImageContentValidator.MaxBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = ImageContentValidator.MaxBytes)]
    public async Task<ActionResult<NewsDto>> Create(
        [FromForm] CreateNewsRequest request, IFormFile? file, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
        {
            return Unauthorized();
        }

        var (bytes, contentType) = await ReadRequiredImageAsync(file, ct);
        var dto = await _news.CreateAsync(request, bytes, contentType, userId.Value, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [Authorize(Roles = Roles.Admin)]
    [HttpPut("{id:long}")]
    [RequestSizeLimit(ImageContentValidator.MaxBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = ImageContentValidator.MaxBytes)]
    public async Task<ActionResult<NewsDto>> Update(
        long id, [FromForm] UpdateNewsRequest request, IFormFile? file, CancellationToken ct)
    {
        // On update the image is optional — only read/replace when a new file was actually supplied.
        var (bytes, contentType) = file is { Length: > 0 }
            ? await ReadImageAsync(file, ct)
            : (null, null);

        return Ok(await _news.UpdateAsync(id, request, bytes, contentType, ct));
    }

    [Authorize(Roles = Roles.Admin)]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await _news.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Reads a required multipart image (create). Rejects a missing/empty/oversize file up front with a
    /// clean 400 (field "file"), mirroring <see cref="CourtImagesController"/>; the service re-validates the bytes.</summary>
    private static async Task<(byte[] Bytes, string ContentType)> ReadRequiredImageAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            throw new ValidationException(
                "An image is required.",
                new Dictionary<string, string[]> { ["file"] = new[] { "An image is required." } });
        }

        return await ReadImageAsync(file, ct);
    }

    /// <summary>Materializes the uploaded file into a byte[], rejecting oversize uploads before allocating the body.</summary>
    private static async Task<(byte[] Bytes, string ContentType)> ReadImageAsync(IFormFile file, CancellationToken ct)
    {
        if (file.Length > ImageContentValidator.MaxBytes)
        {
            throw new ValidationException(
                "Image too large.",
                new Dictionary<string, string[]>
                {
                    ["file"] = new[] { $"Image must be at most {ImageContentValidator.MaxBytes / (1024 * 1024)} MB." },
                });
        }

        using var ms = new MemoryStream((int)file.Length);
        await file.CopyToAsync(ms, ct);
        return (ms.ToArray(), file.ContentType);
    }
}
