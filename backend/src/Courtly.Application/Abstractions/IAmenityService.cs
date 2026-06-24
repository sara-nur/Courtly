using Courtly.Contracts.Common;
using Courtly.Contracts.Reference;

namespace Courtly.Application.Abstractions;

/// <summary>
/// Reference-data CRUD for <c>Amenity</c> (feature 9). Controllers only model-bind, call these, and return the
/// DTO. Failures are signalled by throwing the app's custom exceptions (feature 6): <c>NotFoundException</c>
/// when missing, <c>ConflictException</c> for a duplicate Name, <c>BusinessException</c> when a delete is
/// blocked by referencing court-amenities. Returns DTOs only — never entities.
/// </summary>
public interface IAmenityService
{
    /// <summary>One page of amenities, newest-first, optionally filtered by a case-insensitive name search.</summary>
    Task<PagedResult<AmenityDto>> GetPagedAsync(
        PaginationQuery pagination, string? search, CancellationToken ct = default);

    /// <summary>Single amenity by id; throws <c>NotFoundException</c> if it does not exist.</summary>
    Task<AmenityDto> GetByIdAsync(long id, CancellationToken ct = default);

    Task<AmenityDto> CreateAsync(CreateAmenityRequest request, CancellationToken ct = default);

    Task<AmenityDto> UpdateAsync(long id, UpdateAmenityRequest request, CancellationToken ct = default);

    Task DeleteAsync(long id, CancellationToken ct = default);
}
