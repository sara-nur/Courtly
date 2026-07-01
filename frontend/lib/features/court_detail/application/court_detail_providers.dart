import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../court_catalog/application/court_providers.dart';
import '../../court_catalog/domain/court_models.dart';
import '../../reviews/application/review_providers.dart';
import '../../reviews/domain/review_models.dart';

/// How many reviews the detail screen shows inline before the "See all" link.
const int kReviewPreviewCount = 3;

/// Everything the court-detail screen needs, loaded together so the screen has a
/// single [AsyncValue] to render (mirrors the Home feed's parallel-load pattern).
class CourtDetailData {
  const CourtDetailData({
    required this.court,
    required this.amenities,
    required this.reviews,
    required this.eligibility,
  });

  final Court court;
  final List<CourtAmenityLink> amenities;

  /// First page of reviews (preview) — [PagedResult.totalCount] drives the
  /// "See all N reviews" affordance.
  final PagedResult<Review> reviews;

  /// Whether the signed-in user may post a review for this court.
  final ReviewEligibility eligibility;
}

/// Loads the court, its amenities, the first page of reviews and the caller's
/// review-eligibility **in parallel** ([Future.wait]) for one court. Invalidate
/// `courtDetailProvider(courtId)` after posting a review so the rating, review
/// list and eligibility all refresh.
final courtDetailProvider =
    FutureProvider.family<CourtDetailData, int>((ref, courtId) async {
  final courtRepo = ref.watch(courtRepositoryProvider);
  final reviewRepo = ref.watch(reviewRepositoryProvider);

  final results = await Future.wait([
    courtRepo.getById(courtId),
    courtRepo.listAmenities(courtId),
    reviewRepo.listForCourt(courtId, page: 1, pageSize: kReviewPreviewCount),
    reviewRepo.eligibility(courtId),
  ]);

  return CourtDetailData(
    court: results[0] as Court,
    amenities: results[1] as List<CourtAmenityLink>,
    reviews: results[2] as PagedResult<Review>,
    eligibility: results[3] as ReviewEligibility,
  );
});
