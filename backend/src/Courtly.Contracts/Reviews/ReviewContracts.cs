namespace Courtly.Contracts.Reviews;

/// <summary>
/// Court-review contracts (feature 24). DTOs only on the wire — the service maps these onto the <c>Review</c> entity and
/// never returns it. <see cref="ReviewDto"/> is the read shape shown on the court-detail screen (reviewer name + rating
/// + comment + date); the raw user id is never exposed. <see cref="CreateReviewRequest"/> carries only the reservation
/// id (the court and owner are derived server-side, never trusted from the body — rubric §7.1); server-side validation
/// (rating range, comment length) lives in the FluentValidation validator. <see cref="ReviewEligibilityDto"/> tells the
/// client whether the signed-in user may post a review for a court — true only when they have a <c>Completed</c>
/// reservation for it that has not already been reviewed — and which reservation to post against.
/// </summary>
public sealed record ReviewDto(
    long Id,
    long CourtId,
    string ReviewerName,
    int Rating,
    string? Comment,
    DateTime CreatedAtUtc);

/// <summary>The write payload: a rating (1–5) + optional comment for one of the caller's completed reservations.</summary>
public sealed record CreateReviewRequest(
    long ReservationId,
    int Rating,
    string? Comment);

/// <summary>Whether the caller can review a court, and the reservation id to post the review against (null when not).</summary>
public sealed record ReviewEligibilityDto(
    bool CanReview,
    long? ReservationId);
