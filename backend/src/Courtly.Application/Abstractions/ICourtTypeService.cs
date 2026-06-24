using Courtly.Contracts.Common;
using Courtly.Contracts.Reference;

namespace Courtly.Application.Abstractions;

/// <summary>
/// Reference-data CRUD for <c>CourtType</c> (feature 9). Controllers only model-bind, call these, and return the
/// DTO. Failures are signalled by throwing the app's custom exceptions (feature 6): <c>NotFoundException</c>
/// when missing, <c>ConflictException</c> for a duplicate Name, <c>BusinessException</c> when a delete is blocked
/// by referencing courts. Returns DTOs only — never entities. No lookup/cache (Country-only).
/// </summary>
public interface ICourtTypeService
{
    /// <summary>One page of court types, newest-first, optionally filtered by a case-insensitive name search.</summary>
    Task<PagedResult<CourtTypeDto>> GetPagedAsync(
        PaginationQuery pagination, string? search, CancellationToken ct = default);

    /// <summary>Single court type by id; throws <c>NotFoundException</c> if it does not exist.</summary>
    Task<CourtTypeDto> GetByIdAsync(long id, CancellationToken ct = default);

    Task<CourtTypeDto> CreateAsync(CreateCourtTypeRequest request, CancellationToken ct = default);

    Task<CourtTypeDto> UpdateAsync(long id, UpdateCourtTypeRequest request, CancellationToken ct = default);

    Task DeleteAsync(long id, CancellationToken ct = default);
}
