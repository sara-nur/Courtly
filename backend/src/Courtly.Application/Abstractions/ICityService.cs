using Courtly.Contracts.Common;
using Courtly.Contracts.Reference;

namespace Courtly.Application.Abstractions;

/// <summary>
/// Reference-data CRUD for <c>City</c> (feature 9). Controllers only model-bind, call these, and return the
/// DTO. Failures are signalled by throwing the app's custom exceptions (feature 6): <c>NotFoundException</c>
/// when the city (or its referenced country) is missing, <c>ConflictException</c> for a duplicate name,
/// <c>BusinessException</c> when a delete is blocked by referencing courts. Returns DTOs only — never entities.
/// Mirrors <see cref="ICountryService"/> without the cached lookup (Country-only).
/// </summary>
public interface ICityService
{
    /// <summary>One page of cities, newest-first, optionally filtered by a case-insensitive name search.</summary>
    Task<PagedResult<CityDto>> GetPagedAsync(
        PaginationQuery pagination, string? search, CancellationToken ct = default);

    /// <summary>Single city by id; throws <c>NotFoundException</c> if it does not exist.</summary>
    Task<CityDto> GetByIdAsync(long id, CancellationToken ct = default);

    Task<CityDto> CreateAsync(CreateCityRequest request, CancellationToken ct = default);

    Task<CityDto> UpdateAsync(long id, UpdateCityRequest request, CancellationToken ct = default);

    Task DeleteAsync(long id, CancellationToken ct = default);
}
