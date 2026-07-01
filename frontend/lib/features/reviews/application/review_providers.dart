import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/network/dio_client.dart';
import '../../reference_data/domain/reference_models.dart' show PagedResult;
import '../data/review_api.dart';
import '../data/review_repository.dart';
import '../domain/review_models.dart';

/// Transport over the review endpoints, bound to the app Dio client
/// (mirrors `courtApiProvider`).
final reviewApiProvider = Provider<ReviewApi>(
  (ref) => ReviewApi(ref.watch(dioProvider)),
);

/// The review repository (mirrors `courtRepositoryProvider`).
final reviewRepositoryProvider = Provider<ReviewRepository>(
  (ref) => ReviewRepository(ref.watch(reviewApiProvider)),
);

/// Page size for the full "See all reviews" screen.
const int kReviewPageSize = 10;

/// One page of a court's reviews, keyed by court + page (the "See all" screen
/// holds the current page locally and re-watches as it changes). Invalidate the
/// whole family after a successful post so the list reflects the new review.
final courtReviewsPageProvider =
    FutureProvider.family<PagedResult<Review>, ({int courtId, int page})>(
  (ref, query) => ref
      .watch(reviewRepositoryProvider)
      .listForCourt(query.courtId, page: query.page, pageSize: kReviewPageSize),
);
