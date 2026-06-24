import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/network/dio_client.dart';
import '../data/reference_api.dart';
import '../data/reference_repository.dart';
import '../domain/reference_models.dart';

/// Transport over the reference-data endpoints, bound to the app Dio client
/// (mirrors `authApiProvider`).
final referenceApiProvider = Provider<ReferenceApi>(
  (ref) => ReferenceApi(ref.watch(dioProvider)),
);

/// The reference-data repository (mirrors `authRepositoryProvider`).
final referenceRepositoryProvider = Provider<ReferenceRepository>(
  (ref) => ReferenceRepository(ref.watch(referenceApiProvider)),
);

/// Default page size for every reference list (single source — no magic numbers).
const int kReferencePageSize = 20;

/// Immutable state for a paginated, searchable reference list.
///
/// [value] holds the current page's [AsyncValue] (loading/error/data); [page]
/// and [search] are the inputs that produced it, kept outside the [AsyncValue]
/// so they survive a reload's transient loading state.
class ReferenceListState<T> {
  const ReferenceListState({
    required this.value,
    this.page = 1,
    this.search = '',
  });

  final AsyncValue<PagedResult<T>> value;
  final int page;
  final String search;

  ReferenceListState<T> copyWith({
    AsyncValue<PagedResult<T>>? value,
    int? page,
    String? search,
  }) =>
      ReferenceListState<T>(
        value: value ?? this.value,
        page: page ?? this.page,
        search: search ?? this.search,
      );
}

/// Base controller shared by all five entity list controllers. Subclasses only
/// supply [fetch] (the typed repository list call); pagination/search/refresh
/// behavior is identical everywhere, keeping the five providers consistent.
///
/// On a successful create, call [reload] (resets to page 1) so the newest row —
/// the backend orders `OrderByDescending(Id)` — appears on top. On update/delete
/// call [refresh] to re-fetch the current page in place.
abstract class ReferenceListController<T>
    extends Notifier<ReferenceListState<T>> {
  /// Typed page fetch for this entity (a `repository.list{Plural}` call).
  Future<PagedResult<T>> fetch({
    required int page,
    required int pageSize,
    required String? search,
  });

  @override
  ReferenceListState<T> build() {
    // Kick off the first page off the build frame; UI shows the loading state.
    Future.microtask(load);
    return ReferenceListState<T>(value: const AsyncValue.loading());
  }

  Future<void> _fetchPage(int page, String search) async {
    state = state.copyWith(value: const AsyncValue.loading());
    state = ReferenceListState<T>(
      value: await AsyncValue.guard(
        () => fetch(
          page: page,
          pageSize: kReferencePageSize,
          search: search.trim().isEmpty ? null : search.trim(),
        ),
      ),
      page: page,
      search: search,
    );
  }

  /// Loads the current page with the current search (used on first build + retry).
  Future<void> load() => _fetchPage(state.page, state.search);

  /// Applies a new [search] and reloads from page 1.
  Future<void> setSearch(String search) => _fetchPage(1, search);

  /// Advances one page (no-op when there is no next page).
  Future<void> nextPage() {
    final current = state.value.valueOrNull;
    if (current == null || !current.hasNext) return Future.value();
    return _fetchPage(state.page + 1, state.search);
  }

  /// Goes back one page (no-op when already on the first page).
  Future<void> prevPage() {
    if (state.page <= 1) return Future.value();
    return _fetchPage(state.page - 1, state.search);
  }

  /// Re-fetches the current page in place (after an update/delete).
  Future<void> refresh() => _fetchPage(state.page, state.search);

  /// Resets to page 1 and reloads — used after a create so the newest row shows
  /// on top without any manual refresh.
  Future<void> reload() => _fetchPage(1, state.search);
}

// --- Countries ---------------------------------------------------------------

class CountryListController extends ReferenceListController<Country> {
  @override
  Future<PagedResult<Country>> fetch({
    required int page,
    required int pageSize,
    required String? search,
  }) =>
      ref.read(referenceRepositoryProvider).listCountries(
            page: page,
            pageSize: pageSize,
            search: search,
          );
}

final countryListControllerProvider = NotifierProvider<CountryListController,
    ReferenceListState<Country>>(CountryListController.new);

// --- Cities ------------------------------------------------------------------

class CityListController extends ReferenceListController<City> {
  @override
  Future<PagedResult<City>> fetch({
    required int page,
    required int pageSize,
    required String? search,
  }) =>
      ref.read(referenceRepositoryProvider).listCities(
            page: page,
            pageSize: pageSize,
            search: search,
          );
}

final cityListControllerProvider =
    NotifierProvider<CityListController, ReferenceListState<City>>(
        CityListController.new);

// --- Surface types -----------------------------------------------------------

class SurfaceTypeListController extends ReferenceListController<SurfaceType> {
  @override
  Future<PagedResult<SurfaceType>> fetch({
    required int page,
    required int pageSize,
    required String? search,
  }) =>
      ref.read(referenceRepositoryProvider).listSurfaceTypes(
            page: page,
            pageSize: pageSize,
            search: search,
          );
}

final surfaceTypeListControllerProvider = NotifierProvider<
    SurfaceTypeListController,
    ReferenceListState<SurfaceType>>(SurfaceTypeListController.new);

// --- Court types -------------------------------------------------------------

class CourtTypeListController extends ReferenceListController<CourtType> {
  @override
  Future<PagedResult<CourtType>> fetch({
    required int page,
    required int pageSize,
    required String? search,
  }) =>
      ref.read(referenceRepositoryProvider).listCourtTypes(
            page: page,
            pageSize: pageSize,
            search: search,
          );
}

final courtTypeListControllerProvider = NotifierProvider<CourtTypeListController,
    ReferenceListState<CourtType>>(CourtTypeListController.new);

// --- Amenities ---------------------------------------------------------------

class AmenityListController extends ReferenceListController<Amenity> {
  @override
  Future<PagedResult<Amenity>> fetch({
    required int page,
    required int pageSize,
    required String? search,
  }) =>
      ref.read(referenceRepositoryProvider).listAmenities(
            page: page,
            pageSize: pageSize,
            search: search,
          );
}

final amenityListControllerProvider =
    NotifierProvider<AmenityListController, ReferenceListState<Amenity>>(
        AmenityListController.new);

// --- Country lookup ----------------------------------------------------------

/// The full cached country list, feeding the City form's country [DbDropdown]
/// and the City "empty prerequisite" check (no countries → block "+ Add").
/// Invalidate this after creating/deleting a country so the dropdown stays fresh.
final countryLookupProvider = FutureProvider<List<Country>>(
  (ref) => ref.watch(referenceRepositoryProvider).lookupCountries(),
);
