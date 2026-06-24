namespace Courtly.Contracts.Reference;

/// <summary>
/// Reference-data contracts for <c>City</c> (feature 9). <see cref="CityDto.CountryName"/> is projected via a
/// JOIN on the read path (no extra query / N+1). DTOs only on the wire.
/// </summary>
public sealed record CityDto(long Id, string Name, long CountryId, string CountryName);

public sealed record CreateCityRequest(string Name, long CountryId);

public sealed record UpdateCityRequest(string Name, long CountryId);
