namespace Courtly.Contracts.Reference;

/// <summary>
/// Reference-data contracts for <c>Country</c> (feature 9). DTOs only on the wire — services project
/// entities to <see cref="CountryDto"/> and never return the entity. Server-side validation (NotEmpty,
/// length, ISO-code format) lives in the FluentValidation validators, not here.
/// </summary>
public sealed record CountryDto(long Id, string Name, string IsoCode);

public sealed record CreateCountryRequest(string Name, string IsoCode);

public sealed record UpdateCountryRequest(string Name, string IsoCode);
