import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/network/dio_client.dart';
import '../data/recommendations_api.dart';
import '../data/recommendations_repository.dart';
import '../domain/recommendation_models.dart';

/// Transport over the recommender endpoints, bound to the app Dio client
/// (mirrors `courtApiProvider`).
final recommendationsApiProvider = Provider<RecommendationsApi>(
  (ref) => RecommendationsApi(ref.watch(dioProvider)),
);

/// The recommender repository (normalizes failures to `ApiException`).
final recommendationsRepositoryProvider = Provider<RecommendationsRepository>(
  (ref) => RecommendationsRepository(ref.watch(recommendationsApiProvider)),
);

/// How many recommendations the screen requests (grouped into reason sections
/// client-side). Single source — no magic number in the screen.
const int kRecommendationsPageSize = 20;

/// The current user's recommendation batch (summary + ranked items). Invalidate
/// this after submitting Yes/No feedback so the list re-ranks (a "No" removes the
/// shown courts on the next fetch).
final recommendationsProvider = FutureProvider<RecommendationBatch>(
  (ref) => ref
      .watch(recommendationsRepositoryProvider)
      .list(pageSize: kRecommendationsPageSize),
);
