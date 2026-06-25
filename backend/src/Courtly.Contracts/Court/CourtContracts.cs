namespace Courtly.Contracts.Court;

/// <summary>
/// Court-catalog contracts (feature 10, extended in feature 11). <see cref="CourtDto"/> carries the FK ids plus the
/// resolved nav names (<c>CityName</c>/<c>CountryName</c>/<c>SurfaceTypeName</c>/<c>CourtTypeName</c>), all projected
/// via JOINs on the read path (no extra query / N+1). As of F11 <see cref="CourtDto.PrimaryImageUrl"/> is POPULATED
/// to the relative <c>/api/images/{primaryImageId}</c> (or <c>null</c> when the court has no images) — the bytes are
/// served by a dedicated public endpoint, never base64'd into the list payload. <see cref="CourtDto.Latitude"/>/
/// <see cref="CourtDto.Longitude"/> carry the optional map location (both-or-neither). DTOs only on the wire.
/// </summary>
public sealed record CourtDto(
    long Id,
    string Name,
    string? Description,
    long CityId,
    string CityName,
    long CountryId,
    string CountryName,
    long SurfaceTypeId,
    string SurfaceTypeName,
    long CourtTypeId,
    string CourtTypeName,
    bool IsIndoor,
    bool IsActive,
    bool IsFeatured,
    decimal HourlyPrice,
    string? PrimaryImageUrl,
    double? Latitude,
    double? Longitude);

public sealed record CreateCourtRequest(
    string Name,
    string? Description,
    long CityId,
    long SurfaceTypeId,
    long CourtTypeId,
    bool IsIndoor,
    bool IsActive,
    bool IsFeatured,
    decimal HourlyPrice,
    double? Latitude = null,
    double? Longitude = null);

public sealed record UpdateCourtRequest(
    string Name,
    string? Description,
    long CityId,
    long SurfaceTypeId,
    long CourtTypeId,
    bool IsIndoor,
    bool IsActive,
    bool IsFeatured,
    decimal HourlyPrice,
    double? Latitude = null,
    double? Longitude = null);

/// <summary>
/// Query-string filters for the court list (feature 10), bound via <c>[FromQuery]</c>. Every filter is applied at
/// the database (Where clause) — never in memory. <c>CountryId</c> filters through the <c>City.CountryId</c> nav.
/// All nullable: an omitted filter is simply not applied.
/// </summary>
public sealed record CourtListQuery(
    string? Search,
    long? CityId,
    long? CountryId,
    long? SurfaceTypeId,
    long? CourtTypeId,
    bool? IsIndoor,
    bool? IsActive,
    decimal? MinPrice,
    decimal? MaxPrice,
    bool? IsFeatured);
