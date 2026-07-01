using Courtly.Contracts.Common;
using Courtly.Contracts.Court;

namespace Courtly.Application.Abstractions;

/// <summary>
/// Court-catalog CRUD (feature 10). Controllers only model-bind, call these, and return the DTO. Failures are
/// signalled by throwing the app's custom exceptions (feature 6): <c>NotFoundException</c> when the court (or a
/// referenced City/SurfaceType/CourtType) is missing, <c>BusinessException</c> when a delete is blocked by
/// referencing reservations or time slots. Returns DTOs only — never entities. Mirrors the feature 9 reference
/// services (<see cref="ICityService"/>) with a richer filter set on the list endpoint.
/// </summary>
public interface ICourtService
{
    /// <summary>One page of courts, newest-first, with every filter (search, city, country, surface type, court
    /// type, indoor, active, featured, price range) applied at the database.</summary>
    Task<PagedResult<CourtDto>> GetPagedAsync(
        PaginationQuery pagination, CourtListQuery filter, CancellationToken ct = default);

    /// <summary>Single court by id; throws <c>NotFoundException</c> if it does not exist.</summary>
    Task<CourtDto> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>The courts with the given ids projected to <see cref="CourtDto"/> in a single query (order not
    /// guaranteed — the caller re-orders). Missing ids are simply absent. Read-only; reuses the same nav-JOIN
    /// projection as the list path so callers (e.g. the F29 recommender) never re-implement it.</summary>
    Task<IReadOnlyList<CourtDto>> GetByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default);

    Task<CourtDto> CreateAsync(CreateCourtRequest request, CancellationToken ct = default);

    Task<CourtDto> UpdateAsync(long id, UpdateCourtRequest request, CancellationToken ct = default);

    Task DeleteAsync(long id, CancellationToken ct = default);
}
