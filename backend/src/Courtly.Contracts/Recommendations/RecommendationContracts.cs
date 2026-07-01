using Courtly.Contracts.Common;
using Courtly.Contracts.Court;
using Courtly.Domain.Enums;

namespace Courtly.Contracts.Recommendations;

/// <summary>
/// Recommender contracts (feature 29). <see cref="RecommendationDto"/> pairs a full <see cref="CourtDto"/> (reused
/// from the catalog so the client renders the same card) with the court's recommendation <see cref="Score"/>, its
/// explainable reason — a machine <see cref="ReasonCode"/> (the client groups by this) plus a ready-to-display human
/// <see cref="Reason"/> string — and the caller's current per-court feedback (<see cref="UserFeedback"/>: null = not
/// rated, true = liked, false = disliked), so the client can highlight the active thumb. DTOs only on the wire; the
/// raw score is exposed for transparency/debugging, not shown in the UI.
/// </summary>
public sealed record RecommendationDto(
    CourtDto Court,
    double Score,
    RecommendationReason ReasonCode,
    string Reason,
    bool? UserFeedback);

/// <summary>
/// The one-line explanation shown atop the recommendations screen. <see cref="IsContentBased"/> drives the
/// "Content-Based Filtering Active" badge; it is <c>false</c> on cold-start (no history → popularity fallback).
/// <see cref="BasedOnBookings"/> is the number of non-cancelled reservations the profile was built from (0 when the
/// user has only searched, or is brand new).
/// </summary>
public sealed record RecommendationSummaryDto(
    string Message,
    bool IsContentBased,
    int BasedOnBookings);

/// <summary>
/// The recommendations endpoint envelope: the explainable <see cref="Summary"/> plus a <see cref="PagedResult{T}"/>
/// of ranked recommendations (paginated, max page size enforced — rubric §8.2).
/// </summary>
public sealed record RecommendationsResponse(
    RecommendationSummaryDto Summary,
    PagedResult<RecommendationDto> Page);

/// <summary>
/// The "Are these recommendations helpful?" Yes/No feedback (feature 29). <see cref="IsHelpful"/> is the thumb
/// direction; <see cref="CourtIds"/> are the courts currently shown to the user — a "No" demotes/excludes exactly
/// those from the next fetch, a "Yes" reinforces them. Feedback is upserted per (user, court); the owner comes from
/// the JWT, never this body.
/// </summary>
public sealed record RecommendationFeedbackRequest(
    bool IsHelpful,
    IReadOnlyList<long> CourtIds);
