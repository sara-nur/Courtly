namespace Courtly.Contracts.Reference;

/// <summary>Reference-data contracts for <c>SurfaceType</c> (feature 9). DTOs only on the wire.</summary>
public sealed record SurfaceTypeDto(long Id, string Name, string? Description);

public sealed record CreateSurfaceTypeRequest(string Name, string? Description);

public sealed record UpdateSurfaceTypeRequest(string Name, string? Description);
