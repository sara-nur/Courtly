namespace Courtly.Contracts.Common;

/// <summary>
/// One page of a list endpoint's results. Every list endpoint returns this (rubric §8.2: pagination is
/// mandatory, no unbounded RetrieveAll). <c>Items</c> carries only display-sized DTOs — never entities,
/// PDF blobs, or base64 images (those live on dedicated detail endpoints).
/// </summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    /// <summary>Total number of pages for <see cref="TotalCount"/> at this <see cref="PageSize"/>.</summary>
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < TotalPages;
}
