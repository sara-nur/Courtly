import 'dart:developer' as developer;

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../court_catalog/application/court_providers.dart';
import '../../court_catalog/domain/court_models.dart';
import '../../search_history/application/search_history_providers.dart';

/// Page size for the client search results list (single source — no magic
/// numbers; mirrors `kCourtPageSize` from the catalog feature).
const int kSearchPageSize = 20;

/// Immutable filter set applied to the client court search. Every field is
/// optional so a null/empty value means "don't filter on this". This is the
/// client-facing subset of the catalog's `CourtFilters` (no admin-only fields
/// like `isActive`/`underMaintenance`): a free-text [query], the surface/court
/// type FK ids, a min/max hourly price band, an [indoorOnly] toggle and a
/// [minRating] floor.
class CourtSearchFilters {
  const CourtSearchFilters({
    this.query,
    this.surfaceTypeId,
    this.courtTypeId,
    this.minPrice,
    this.maxPrice,
    this.indoorOnly,
    this.minRating,
  });

  final String? query;
  final int? surfaceTypeId;
  final int? courtTypeId;
  final double? minPrice;
  final double? maxPrice;
  final bool? indoorOnly;
  final double? minRating;

  /// True when no filter is set (the search is "show everything"). A blank/
  /// whitespace-only [query] counts as empty.
  bool get isEmpty =>
      (query == null || query!.trim().isEmpty) &&
      surfaceTypeId == null &&
      courtTypeId == null &&
      minPrice == null &&
      maxPrice == null &&
      indoorOnly == null &&
      minRating == null;

  /// Rebuilds the filter set. Each field takes a sentinel-free pair so a caller
  /// can clear a value back to null (e.g. `surfaceTypeId: null, clearSurfaceType:
  /// true`) — mirrors the catalog `CourtFilters.copyWith` idiom.
  CourtSearchFilters copyWith({
    String? query,
    bool clearQuery = false,
    int? surfaceTypeId,
    bool clearSurfaceType = false,
    int? courtTypeId,
    bool clearCourtType = false,
    double? minPrice,
    bool clearMinPrice = false,
    double? maxPrice,
    bool clearMaxPrice = false,
    bool? indoorOnly,
    bool clearIndoorOnly = false,
    double? minRating,
    bool clearMinRating = false,
  }) =>
      CourtSearchFilters(
        query: clearQuery ? null : (query ?? this.query),
        surfaceTypeId:
            clearSurfaceType ? null : (surfaceTypeId ?? this.surfaceTypeId),
        courtTypeId: clearCourtType ? null : (courtTypeId ?? this.courtTypeId),
        minPrice: clearMinPrice ? null : (minPrice ?? this.minPrice),
        maxPrice: clearMaxPrice ? null : (maxPrice ?? this.maxPrice),
        indoorOnly: clearIndoorOnly ? null : (indoorOnly ?? this.indoorOnly),
        minRating: clearMinRating ? null : (minRating ?? this.minRating),
      );
}

/// Immutable state for the paginated client search.
///
/// [results] holds the current page's [AsyncValue]; [filters] and [page] are
/// the inputs that produced it, kept outside the [AsyncValue] so they survive a
/// reload's transient loading state (mirrors `CourtListState`).
class SearchState {
  const SearchState({
    required this.results,
    this.filters = const CourtSearchFilters(),
    this.page = 1,
  });

  final AsyncValue<PagedResult<Court>> results;
  final CourtSearchFilters filters;
  final int page;

  SearchState copyWith({
    AsyncValue<PagedResult<Court>>? results,
    CourtSearchFilters? filters,
    int? page,
  }) =>
      SearchState(
        results: results ?? this.results,
        filters: filters ?? this.filters,
        page: page ?? this.page,
      );
}

/// Drives the client search screen: holds the filter + page state and re-fetches
/// via [courtRepositoryProvider]. Mirrors the catalog `CourtListController`
/// (auto-load on build, guarded pagination, page-back clamp).
///
/// On an EXPLICIT search only — [applyFilters] and a query submit via
/// [setQuery] — it ALSO records the search to the search-history backend as a
/// fire-and-forget side effect (never awaited in a blocking way; errors only
/// logged). Pagination ([nextPage]/[previousPage]) never records.
class CourtSearchController extends Notifier<SearchState> {
  @override
  SearchState build() {
    Future.microtask(_load);
    return const SearchState(results: AsyncValue.loading());
  }

  /// Fetches [page] with [filters] and stores the result. A blank query is sent
  /// as null so the backend doesn't filter on it. Includes the same page-back
  /// clamp as the catalog: if a page comes back empty while earlier pages still
  /// have rows, step back one page.
  Future<void> _load({CourtSearchFilters? filters, int? page}) async {
    final effectiveFilters = filters ?? state.filters;
    final effectivePage = page ?? state.page;

    state = state.copyWith(results: const AsyncValue.loading());

    final query = effectiveFilters.query?.trim();
    final value = await AsyncValue.guard(
      () => ref.read(courtRepositoryProvider).list(
            page: effectivePage,
            pageSize: kSearchPageSize,
            search: (query == null || query.isEmpty) ? null : query,
            surfaceTypeId: effectiveFilters.surfaceTypeId,
            courtTypeId: effectiveFilters.courtTypeId,
            isIndoor: effectiveFilters.indoorOnly,
            minPrice: effectiveFilters.minPrice,
            maxPrice: effectiveFilters.maxPrice,
            minRating: effectiveFilters.minRating,
          ),
    );

    final result = value.valueOrNull;
    if (result != null && result.items.isEmpty && effectivePage > 1) {
      return _load(filters: effectiveFilters, page: effectivePage - 1);
    }

    state = SearchState(
      results: value,
      filters: effectiveFilters,
      page: effectivePage,
    );
  }

  /// Fire-and-forget record of the current [filters] to the search-history
  /// backend. Never awaited in a blocking way; failures are only logged so a
  /// history hiccup can never break the search itself.
  void _recordHistory(CourtSearchFilters filters) {
    final rawQuery = filters.query?.trim();
    () async {
      try {
        await ref.read(searchHistoryRepositoryProvider).record(
              surfaceTypeId: filters.surfaceTypeId,
              courtTypeId: filters.courtTypeId,
              minPrice: filters.minPrice,
              maxPrice: filters.maxPrice,
              indoorOnly: filters.indoorOnly,
              rawQuery:
                  (rawQuery == null || rawQuery.isEmpty) ? null : rawQuery,
            );
      } catch (e, st) {
        developer.log(
          'Failed to record search history',
          name: 'CourtSearchController',
          error: e,
          stackTrace: st,
        );
      }
    }();
  }

  /// Applies a new [query] and reloads from page 1. This is an EXPLICIT search
  /// (a query submit), so it records to history.
  Future<void> setQuery(String query) {
    final filters = state.filters.copyWith(query: query);
    _recordHistory(filters);
    return _load(filters: filters, page: 1);
  }

  /// Applies a new [filters] set and reloads from page 1. This is an EXPLICIT
  /// search (the filter modal's Apply), so it records to history.
  Future<void> applyFilters(CourtSearchFilters filters) {
    _recordHistory(filters);
    return _load(filters: filters, page: 1);
  }

  /// Clears every filter and reloads from page 1. Not an explicit search — does
  /// not record history.
  Future<void> clearFilters() =>
      _load(filters: const CourtSearchFilters(), page: 1);

  /// Advances one page (no-op when there is no next page). Does NOT record.
  Future<void> nextPage() {
    final current = state.results.valueOrNull;
    if (current == null || !current.hasNext) return Future.value();
    return _load(page: state.page + 1);
  }

  /// Goes back one page (no-op when already on the first page). Does NOT record.
  Future<void> previousPage() {
    if (state.page <= 1) return Future.value();
    return _load(page: state.page - 1);
  }

  /// Re-fetches the current page in place (used as the error-state retry).
  Future<void> retry() => _load();
}

final searchControllerProvider =
    NotifierProvider<CourtSearchController, SearchState>(CourtSearchController.new);
