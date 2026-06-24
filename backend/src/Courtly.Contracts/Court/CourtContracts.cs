namespace Courtly.Contracts.Court;

/// <summary>
/// Court-catalog contracts (feature 10). <see cref="CourtDto"/> carries the FK ids plus the resolved nav names
/// (<c>CityName</c>/<c>CountryName</c>/<c>SurfaceTypeName</c>/<c>CourtTypeName</c>), all projected via JOINs on
/// the read path (no extra query / N+1). <see cref="CourtDto.PrimaryImageUrl"/> is always <c>null</c> in F10:
/// court images are stored as <c>bytea</c> with no public serving endpoint yet — the field exists so the contract
/// stays stable when F11 adds image serving (it does NOT base64 bytes into the list payload). DTOs only on the wire.
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
    string? PrimaryImageUrl);

public sealed record CreateCourtRequest(
    string Name,
    string? Description,
    long CityId,
    long SurfaceTypeId,
    long CourtTypeId,
    bool IsIndoor,
    bool IsActive,
    bool IsFeatured,
    decimal HourlyPrice);

public sealed record UpdateCourtRequest(
    string Name,
    string? Description,
    long CityId,
    long SurfaceTypeId,
    long CourtTypeId,
    bool IsIndoor,
    bool IsActive,
    bool IsFeatured,
    decimal HourlyPrice);

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
