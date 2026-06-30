import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/network/dio_client.dart';
import '../data/search_history_api.dart';
import '../data/search_history_repository.dart';

/// Transport over the search-history endpoint, bound to the app Dio client.
final searchHistoryApiProvider = Provider<SearchHistoryApi>(
  (ref) => SearchHistoryApi(ref.watch(dioProvider)),
);

/// The search-history repository (normalizes failures to `ApiException`).
final searchHistoryRepositoryProvider = Provider<SearchHistoryRepository>(
  (ref) => SearchHistoryRepository(ref.watch(searchHistoryApiProvider)),
);
