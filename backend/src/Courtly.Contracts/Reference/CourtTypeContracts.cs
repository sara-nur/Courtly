namespace Courtly.Contracts.Reference;

/// <summary>Reference-data contracts for <c>CourtType</c> (feature 9). DTOs only on the wire.</summary>
public sealed record CourtTypeDto(long Id, string Name, string? Description);

public sealed record CreateCourtTypeRequest(string Name, string? Description);

public sealed record UpdateCourtTypeRequest(string Name, string? Description);
