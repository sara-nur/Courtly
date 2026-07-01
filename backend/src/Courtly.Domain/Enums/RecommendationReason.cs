namespace Courtly.Domain.Enums;

/// <summary>
/// Why a court was recommended (feature 29). The recommender picks the single strongest scoring dimension for
/// each court and reports it as one of these codes so the client can group recommendations under an explainable
/// heading ("Because you like Clay", "☀️ Morning availability", …) — rubric §2.4: recommendations must be
/// explainable. The first five are content-based signals; <see cref="Popular"/>/<see cref="TopRated"/> are the
/// popularity fallbacks used on cold-start (no history) or when no content signal applies.
/// </summary>
public enum RecommendationReason
{
    Surface = 0,
    CourtType = 1,
    TimeBucket = 2,
    Price = 3,
    Indoor = 4,
    Popular = 5,
    TopRated = 6,
}
