import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/network/dio_client.dart';
import '../data/news_api.dart';
import '../data/news_repository.dart';
import '../domain/news_models.dart';

/// Transport over the news endpoints, bound to the app Dio client.
final newsApiProvider = Provider<NewsApi>(
  (ref) => NewsApi(ref.watch(dioProvider)),
);

/// The news repository (normalizes failures to `ApiException`).
final newsRepositoryProvider = Provider<NewsRepository>(
  (ref) => NewsRepository(ref.watch(newsApiProvider)),
);

/// Default page size for the news list (single source — no magic numbers).
const int kNewsPageSize = 20;

/// The admin list's publish filter: all rows, only published, or only hidden.
/// [value] is the backend `activeOnly` query value (null = no filter).
enum NewsActiveFilter {
  all,
  active,
  hidden;

  bool? get value => switch (this) {
        NewsActiveFilter.all => null,
        NewsActiveFilter.active => true,
        NewsActiveFilter.hidden => false,
      };
}

/// Immutable state for the paginated, searchable, publish-filterable news list.
/// [page]/[search]/[activeFilter] are kept outside the [AsyncValue] so they
/// survive a reload's transient loading state.
class NewsListState {
  const NewsListState({
    required this.value,
    this.page = 1,
    this.search = '',
    this.activeFilter = NewsActiveFilter.all,
  });

  final AsyncValue<PagedResult<News>> value;
  final int page;
  final String search;
  final NewsActiveFilter activeFilter;

  NewsListState copyWith({
    AsyncValue<PagedResult<News>>? value,
    int? page,
    String? search,
    NewsActiveFilter? activeFilter,
  }) =>
      NewsListState(
        value: value ?? this.value,
        page: page ?? this.page,
        search: search ?? this.search,
        activeFilter: activeFilter ?? this.activeFilter,
      );
}

/// Drives the admin news list. On a successful create call [reload] (resets to
/// page 1) so the newest row shows on top; on update/delete call [refresh] to
/// re-fetch the current page in place.
class NewsListController extends Notifier<NewsListState> {
  @override
  NewsListState build() {
    // Kick off the first page off the build frame; UI shows the loading state.
    Future.microtask(load);
    return NewsListState(value: const AsyncValue.loading());
  }

  Future<void> _fetchPage(int page, String search, NewsActiveFilter filter) async {
    state = state.copyWith(value: const AsyncValue.loading());
    state = NewsListState(
      value: await AsyncValue.guard(
        () => ref.read(newsRepositoryProvider).list(
              page: page,
              pageSize: kNewsPageSize,
              search: search.trim().isEmpty ? null : search.trim(),
              activeOnly: filter.value,
            ),
      ),
      page: page,
      search: search,
      activeFilter: filter,
    );
  }

  /// Loads the current page with the current search + filter (first build + retry).
  Future<void> load() => _fetchPage(state.page, state.search, state.activeFilter);

  /// Applies a new [search] and reloads from page 1.
  Future<void> setSearch(String search) =>
      _fetchPage(1, search, state.activeFilter);

  /// Applies a new publish [filter] and reloads from page 1.
  Future<void> setActiveFilter(NewsActiveFilter filter) =>
      _fetchPage(1, state.search, filter);

  /// Advances one page (no-op when there is no next page).
  Future<void> nextPage() {
    final current = state.value.valueOrNull;
    if (current == null || !current.hasNext) return Future.value();
    return _fetchPage(state.page + 1, state.search, state.activeFilter);
  }

  /// Goes back one page (no-op when already on the first page).
  Future<void> prevPage() {
    if (state.page <= 1) return Future.value();
    return _fetchPage(state.page - 1, state.search, state.activeFilter);
  }

  /// Re-fetches the current page in place (after an update/delete).
  Future<void> refresh() =>
      _fetchPage(state.page, state.search, state.activeFilter);

  /// Resets to page 1 and reloads — used after a create so the newest row shows
  /// on top without any manual refresh.
  Future<void> reload() => _fetchPage(1, state.search, state.activeFilter);
}

final newsListControllerProvider =
    NotifierProvider<NewsListController, NewsListState>(NewsListController.new);
