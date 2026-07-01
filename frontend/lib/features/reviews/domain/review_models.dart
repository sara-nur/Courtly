/// Immutable domain models for Feature 24 (client court reviews).
///
/// [Review] mirrors the backend `ReviewDto` (camelCase keys): the reviewer's
/// display name (never a raw user id), a 1–5 [rating], an optional [comment] and
/// the created timestamp. [ReviewEligibility] mirrors `ReviewEligibilityDto` —
/// whether the signed-in user may post a review for a court and the reservation
/// to post against. Manual `fromJson` (the codebase convention — no
/// json_serializable).
library;

/// One posted review shown on the court-detail / reviews screen.
class Review {
  const Review({
    required this.id,
    required this.courtId,
    required this.reviewerName,
    required this.rating,
    this.comment,
    required this.createdAtUtc,
  });

  final int id;
  final int courtId;
  final String reviewerName;
  final int rating;
  final String? comment;
  final DateTime createdAtUtc;

  factory Review.fromJson(Map<String, dynamic> json) => Review(
        id: (json['id'] as num).toInt(),
        courtId: (json['courtId'] as num?)?.toInt() ?? 0,
        reviewerName: json['reviewerName'] as String? ?? 'Anonymous',
        rating: (json['rating'] as num?)?.toInt() ?? 0,
        comment: json['comment'] as String?,
        createdAtUtc: DateTime.parse(json['createdAtUtc'] as String),
      );
}

/// Whether the caller may review a court, and which reservation to post against.
/// [canReview] is true only when the user has a Completed reservation for the
/// court that has not been reviewed yet; [reservationId] is then non-null.
class ReviewEligibility {
  const ReviewEligibility({required this.canReview, this.reservationId});

  final bool canReview;
  final int? reservationId;

  factory ReviewEligibility.fromJson(Map<String, dynamic> json) =>
      ReviewEligibility(
        canReview: json['canReview'] as bool? ?? false,
        reservationId: (json['reservationId'] as num?)?.toInt(),
      );
}
