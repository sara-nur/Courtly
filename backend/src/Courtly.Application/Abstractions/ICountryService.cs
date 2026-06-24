using Courtly.Contracts.Common;
using Courtly.Contracts.Reference;

namespace Courtly.Application.Abstractions;

/// <summary>
/// Reference-data CRUD for <c>Country</c> (feature 9). Controllers only model-bind, call these, and return the
/// DTO. Failures are signalled by throwing the app's custom exceptions (feature 6): <c>NotFoundException</c>
/// when missing, <c>ConflictException</c> for a duplicate Name/IsoCode, <c>BusinessException</c> when a delete
/// is blocked by referencing rows. Returns DTOs only — never entities.
/// </summary>
public interface ICountryService
{
    /// <summary>One page of countries, newest-first, optionally filtered by a case-insensitive name search.</summary>
    Task<PagedResult<CountryDto>> GetPagedAsync(
        PaginationQuery pagination, string? search, CancellationToken ct = default);

    /// <summary>Single country by id; throws <c>NotFoundException</c> if it does not exist.</summary>
    Task<CountryDto> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>Full list (name-ordered) for dropdowns, served from a short-TTL memory cache.</summary>
    Task<IReadOnlyList<CountryDto>> GetLookupAsync(CancellationToken ct = default);

    Task<CountryDto> CreateAsync(CreateCountryRequest request, CancellationToken ct = default);

    Task<CountryDto> UpdateAsync(long id, UpdateCountryRequest request, CancellationToken ct = default);

    Task DeleteAsync(long id, CancellationToken ct = default);
}
