using Courtly.Contracts.Common;
using Courtly.Contracts.Recommendations;

namespace Courtly.Application.Abstractions;

/// <summary>
/// Content-based + popularity recommender (feature 29). Ranks bookable courts for the current user from signals that
/// are really written elsewhere — the user's non-cancelled reservations (surface / court type / time-bucket / price /
/// indoor) and their search history (F23) — with a popularity fallback for cold-start, and reports an explainable
/// reason per court. Yes/No feedback is stored and fed back into scoring (a "No" excludes the shown courts next
/// time). The owner is always the JWT caller; nothing is trusted from the route/body. Returns DTOs only.
/// </summary>
public interface IRecommendationService
{
    /// <summary>One ranked, paginated page of recommendations for the current user, plus an explainable summary.
    /// Unauthenticated callers get an empty result (the controller is <c>[Authorize]</c>, so this is defensive).</summary>
    Task<RecommendationsResponse> GetAsync(PaginationQuery pagination, CancellationToken ct = default);

    /// <summary>Records the user's Yes/No feedback on the currently-shown courts (upsert per (user, court)) and
    /// invalidates their cached preference profile so the next fetch reflects it immediately. A no-op for an
    /// unauthenticated caller.</summary>
    Task RecordFeedbackAsync(RecommendationFeedbackRequest request, CancellationToken ct = default);
}
