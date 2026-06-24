using Courtly.Contracts.Common;
using Courtly.Contracts.Reference;

namespace Courtly.Application.Abstractions;

/// <summary>
/// Reference-data CRUD for <c>SurfaceType</c> (feature 9). Controllers only model-bind, call these, and return
/// the DTO. Failures are signalled by throwing the app's custom exceptions (feature 6): <c>NotFoundException</c>
/// when missing, <c>ConflictException</c> for a duplicate Name, <c>BusinessException</c> when a delete is blocked
/// by referencing courts. Returns DTOs only — never entities. Mirrors <see cref="ICountryService"/> minus the
/// dropdown lookup (surface types are not cached).
/// </summary>
public interface ISurfaceTypeService
{
    /// <summary>One page of surface types, newest-first, optionally filtered by a case-insensitive name search.</summary>
    Task<PagedResult<SurfaceTypeDto>> GetPagedAsync(
        PaginationQuery pagination, string? search, CancellationToken ct = default);

    /// <summary>Single surface type by id; throws <c>NotFoundException</c> if it does not exist.</summary>
    Task<SurfaceTypeDto> GetByIdAsync(long id, CancellationToken ct = default);

    Task<SurfaceTypeDto> CreateAsync(CreateSurfaceTypeRequest request, CancellationToken ct = default);

    Task<SurfaceTypeDto> UpdateAsync(long id, UpdateSurfaceTypeRequest request, CancellationToken ct = default);

    Task DeleteAsync(long id, CancellationToken ct = default);
}
