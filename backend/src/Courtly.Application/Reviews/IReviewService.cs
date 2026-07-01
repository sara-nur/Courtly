using Courtly.Contracts.Common;
using Courtly.Contracts.Reviews;

namespace Courtly.Application.Reviews;

/// <summary>
/// Court reviews (feature 24): the public read list for a court and the guarded write. The owner is always the caller
/// (from the JWT), the court is derived from the reservation, and a review is allowed only on a <c>Completed</c>
/// reservation, once per reservation. See <see cref="ReviewService"/> for the rules.
/// </summary>
public interface IReviewService
{
    /// <summary>One page of a court's reviews, newest-first.</summary>
    Task<PagedResult<ReviewDto>> GetForCourtAsync(
        long courtId, PaginationQuery pagination, CancellationToken ct = default);

    /// <summary>Creates the caller's review for one of their completed reservations.</summary>
    Task<ReviewDto> CreateAsync(CreateReviewRequest request, CancellationToken ct = default);

    /// <summary>Whether the caller may review the given court, and the reservation to post against.</summary>
    Task<ReviewEligibilityDto> GetEligibilityAsync(long courtId, CancellationToken ct = default);
}
