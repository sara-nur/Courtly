using Courtly.Contracts.Court;

namespace Courtly.Application.Courts.Media;

/// <summary>
/// Court media + amenities (feature 11). Images are stored as <c>bytea</c> and served by id; this service owns the
/// primary-image invariant (exactly one primary while a court has images), the amenity replace-set semantics, and
/// the raw-bytes read used by the public image endpoint. Controllers only model-bind and call these; failures are
/// signalled with the app's exceptions (<c>NotFoundException</c> for a missing court/image/amenity,
/// <c>ValidationException</c> for a rejected upload). Returns DTOs only — never entities.
/// </summary>
public interface ICourtMediaService
{
    /// <summary>All images for a court, primary first then by id; throws <c>NotFoundException</c> if the court
    /// does not exist. Each DTO's <c>Url</c> is the relative <c>/api/images/{id}</c>.</summary>
    Task<IReadOnlyList<CourtImageDto>> GetImagesAsync(long courtId, CancellationToken ct = default);

    /// <summary>Validates and stores an uploaded image. If the court has no images yet OR
    /// <paramref name="isPrimary"/> is requested, the new image becomes primary and all others are unset.</summary>
    Task<CourtImageDto> AddImageAsync(
        long courtId, byte[] bytes, string? declaredContentType, string? caption, bool isPrimary,
        CancellationToken ct = default);

    /// <summary>Makes the given image the court's primary, unsetting the rest.</summary>
    Task<CourtImageDto> SetPrimaryImageAsync(long courtId, long imageId, CancellationToken ct = default);

    /// <summary>Deletes an image; if it was primary, promotes the earliest remaining image (lowest id) to
    /// primary.</summary>
    Task DeleteImageAsync(long courtId, long imageId, CancellationToken ct = default);

    /// <summary>All amenities attached to a court, with the resolved amenity name/icon and per-court payload.</summary>
    Task<IReadOnlyList<CourtAmenityDto>> GetAmenitiesAsync(long courtId, CancellationToken ct = default);

    /// <summary>Replaces the court's whole amenity set with the request set (every AmenityId must exist).</summary>
    Task<IReadOnlyList<CourtAmenityDto>> SetAmenitiesAsync(
        long courtId, SetCourtAmenitiesRequest request, CancellationToken ct = default);

    /// <summary>Raw image bytes + content-type for the public serving endpoint; null when the id does not exist.</summary>
    Task<ImageContent?> GetImageContentAsync(long imageId, CancellationToken ct = default);
}

/// <summary>Raw image payload for the public <c>GET /api/images/{id}</c> endpoint (bytes + content-type).</summary>
public readonly record struct ImageContent(byte[] Bytes, string ContentType);
