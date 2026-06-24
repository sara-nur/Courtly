namespace Courtly.Contracts.Reference;

/// <summary>Reference-data contracts for <c>Amenity</c> (feature 9). DTOs only on the wire.</summary>
public sealed record AmenityDto(long Id, string Name, string? IconKey);

public sealed record CreateAmenityRequest(string Name, string? IconKey);

public sealed record UpdateAmenityRequest(string Name, string? IconKey);
