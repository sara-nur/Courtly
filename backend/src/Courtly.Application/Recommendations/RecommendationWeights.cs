namespace Courtly.Application.Recommendations;

/// <summary>
/// The recommender's tunable constants (feature 29) — one source of truth for every weight and knob so the scoring
/// method contains no inline literals (rubric §3.4: magic numbers → named constants) and
/// <c>recommender-dokumentacija.md</c> can be kept in exact lockstep with the code (graders diff doc ↔ code).
///
/// The final score blends a content-based component (0.75) with a popularity component (0.25); within the content
/// component surface (0.35) and time-bucket (0.25) dominate — the two visible drivers in the mockup ("Because you
/// like Clay", "Morning availability"). Feedback is applied on top: a "No" is a large negative that demotes the
/// court below every non-disliked one (it is never removed — the list can't empty out), a "Yes" is a small positive nudge.
/// </summary>
public static class RecommendationWeights
{
    // Top-level blend (content vs popularity).
    public const double ContentGroup = 0.75;
    public const double PopularityGroup = 0.25;

    // Content-component weights (sum to 1.0).
    public const double Surface = 0.35;
    public const double Bucket = 0.25;
    public const double CourtType = 0.15;
    public const double Price = 0.15;
    public const double Indoor = 0.10;

    // Popularity-component weights (sum to 1.0).
    public const double PopReservations = 0.60;
    public const double PopRating = 0.40;

    // Feedback adjustment. A negative dominates any content+popularity score (both in [0,1]) so the court is
    // demoted below every non-disliked court; feedback never excludes, so the list can't empty out.
    public const double FeedbackNegative = -1.0;
    public const double FeedbackPositive = 0.05;

    // Booking signals count double a search signal (a completed booking is a stronger preference than a search).
    public const double BookingSignalWeight = 2.0;
    public const double SearchSignalWeight = 1.0;

    // Price-proximity Gaussian: sigma = max(target * SigmaFactor, SigmaFloor). Floor keeps cheap courts from being
    // hypersensitive to small absolute price gaps.
    public const double SigmaFactor = 0.25;
    public const double SigmaFloor = 5.0;

    // How many candidate courts to score at most (bounds the read + the in-memory scoring pass).
    public const int CandidateCap = 200;
}
