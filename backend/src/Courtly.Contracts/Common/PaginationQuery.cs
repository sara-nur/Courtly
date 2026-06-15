namespace Courtly.Contracts.Common;

/// <summary>
/// Page/pageSize query parameters shared by every list endpoint. <see cref="Normalize"/> enforces the
/// server-side bounds (rubric §8.2: <c>PageSize</c> must have a hard maximum — here <see cref="MaxPageSize"/>),
/// so a client cannot request an unbounded page. The clamp is pure (no EF) and lives here so it can be
/// unit-tested without a database.
/// </summary>
public sealed record PaginationQuery
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>Returns a copy with <c>Page ≥ 1</c> and <c>1 ≤ PageSize ≤ MaxPageSize</c> (invalid sizes fall
    /// back to the default, oversized requests are clamped to the maximum).</summary>
    public PaginationQuery Normalize()
    {
        var page = Page < 1 ? 1 : Page;
        var pageSize = PageSize switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => PageSize,
        };
        return this with { Page = page, PageSize = pageSize };
    }

    /// <summary>Rows to skip for the (already-normalized) page.</summary>
    public int Skip => (Page - 1) * PageSize;
}
