using Courtly.Application.Courts.Media;
using Courtly.Contracts.Common;
using Courtly.Contracts.News;

namespace Courtly.Application.Abstractions;

/// <summary>
/// News / announcement CRUD (feature 21). Controllers only model-bind, hand over the uploaded image bytes, call
/// these, and return the DTO. Failures are signalled by throwing the app's custom exceptions (feature 6):
/// <c>NotFoundException</c> when missing, <c>ValidationException</c> for a bad image (MIME / magic bytes / size).
/// The author is taken from the JWT (passed in by the controller), never from the request body. Returns DTOs only —
/// never entities or raw image bytes (those stream from the dedicated image endpoint).
/// </summary>
public interface INewsService
{
    /// <summary>One admin page of news, newest-first by published date, optionally filtered by a case-insensitive
    /// Title/Text search and by active state (<c>activeOnly</c> null = both published and hidden).</summary>
    Task<PagedResult<NewsDto>> GetPagedAsync(
        PaginationQuery pagination, string? search, bool? activeOnly, CancellationToken ct = default);

    /// <summary>One client page of <em>published</em> news (active and already published), newest-first. Consumed by
    /// the client home card (feature 23).</summary>
    Task<PagedResult<NewsDto>> GetPublishedAsync(PaginationQuery pagination, CancellationToken ct = default);

    /// <summary>Single news item by id; throws <c>NotFoundException</c> if it does not exist.</summary>
    Task<NewsDto> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>Creates a news item. The image is required: <paramref name="imageBytes"/> is validated
    /// (MIME + magic bytes + size) before the row is written.</summary>
    Task<NewsDto> CreateAsync(
        CreateNewsRequest request, byte[] imageBytes, string contentType, Guid authorId, CancellationToken ct = default);

    /// <summary>Updates a news item. The image is replaced only when <paramref name="imageBytes"/> is supplied;
    /// otherwise the existing image is kept.</summary>
    Task<NewsDto> UpdateAsync(
        long id, UpdateNewsRequest request, byte[]? imageBytes, string? contentType, CancellationToken ct = default);

    /// <summary>The raw image bytes + content-type for a news item, or null when the id does not exist. Streamed by
    /// the public image endpoint.</summary>
    Task<ImageContent?> GetImageContentAsync(long id, CancellationToken ct = default);

    Task DeleteAsync(long id, CancellationToken ct = default);
}
