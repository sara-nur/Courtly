using Courtly.Contracts.Common;
using Microsoft.EntityFrameworkCore;

namespace Courtly.Application.Common.Pagination;

/// <summary>Pagination helpers for the read path used by every list endpoint (rubric §8.2).</summary>
public static class QueryableExtensions
{
    /// <summary>
    /// Materializes one page of <paramref name="query"/> into a <see cref="PagedResult{T}"/>, clamping the
    /// request first (<see cref="PaginationQuery.Normalize"/>) so <c>pageSize</c> can never exceed
    /// <see cref="PaginationQuery.MaxPageSize"/>. Counts and pages at the database (one <c>COUNT</c> + one
    /// windowed read), never by loading everything into memory.
    /// </summary>
    /// <remarks>Call on an <c>AsNoTracking()</c> entity query projected to a DTO (rubric: no entities on the
    /// wire, no change-tracking on reads). The extension itself adds no tracking.</remarks>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query,
        PaginationQuery pagination,
        CancellationToken ct = default)
    {
        var page = pagination.Normalize();

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(ct);

        return new PagedResult<T>(items, page.Page, page.PageSize, totalCount);
    }
}
