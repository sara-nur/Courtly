namespace Courtly.Contracts.Court;

/// <summary>
/// Court media + amenity contracts (feature 11). Image bytes are NEVER on the wire here: a <see cref="CourtImageDto"/>
/// carries only a relative <see cref="Url"/> (<c>/api/images/{id}</c>) the Flutter app prefixes with its configured
/// base URL, and the raw bytes are served separately by the public <c>GET /api/images/{id}</c> endpoint. DTOs only.
/// </summary>
public sealed record CourtImageDto(
    long Id,
    long CourtId,
    string Url,
    bool IsPrimary,
    string? Caption);

/// <summary>
/// A court-amenity link with its resolved amenity <see cref="AmenityName"/>/<see cref="IconKey"/> (projected via a
/// JOIN, no N+1) plus the per-court payload (<see cref="Note"/>, <see cref="IsHighlighted"/>). DTOs only on the wire.
/// </summary>
public sealed record CourtAmenityDto(
    long Id,
    long CourtId,
    long AmenityId,
    string AmenityName,
    string? IconKey,
    string? Note,
    bool IsHighlighted);

/// <summary>
/// Replace-the-whole-set request body for <c>PUT /api/courts/{id}/amenities</c>. The court's amenity rows are
/// replaced wholesale with <see cref="Amenities"/> (no partial add/remove). Validated server-side: every
/// <c>AmenityId &gt; 0</c>, the ids are DISTINCT, and each <c>Note</c> is at most 300 characters.
/// </summary>
public sealed record SetCourtAmenitiesRequest(
    IReadOnlyList<CourtAmenityInput> Amenities);

/// <summary>One amenity to attach to a court, with its optional per-court payload.</summary>
public sealed record CourtAmenityInput(
    long AmenityId,
    string? Note,
    bool IsHighlighted);
