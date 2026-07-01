import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/network/dio_client.dart';
import '../../reference_data/application/reference_providers.dart';
import '../../reference_data/domain/reference_models.dart';
import '../data/court_api.dart';
import '../data/court_repository.dart';
import '../domain/court_models.dart';

/// Transport over the court endpoints, bound to the app Dio client
/// (mirrors `referenceApiProvider`).
final courtApiProvider = Provider<CourtApi>(
  (ref) => CourtApi(ref.watch(dioProvider)),
);

/// The court repository (mirrors `referenceRepositoryProvider`).
final courtRepositoryProvider = Provider<CourtRepository>(
  (ref) => CourtRepository(ref.watch(courtApiProvider)),
);

/// Default page size for the court grid (single source — no magic numbers).
const int kCourtPageSize = 20;

/// Page size used for the FK dropdown lookups. Reference tables are small, so a
/// single large page is the practical "lookup" (no dedicated lookup endpoint
/// exists for cities/surface-types/court-types — see recon §6).
const int kLookupPageSize = 100;

/// Immutable filter set applied to the court list. Every field is optional so a
/// null/empty value means "don't filter on this".
class CourtFilters {
  const CourtFilters({
    this.search = '',
    this.cityId,
    this.countryId,
    this.surfaceTypeId,
    this.courtTypeId,
    this.isIndoor,
    this.isActive,
    this.isFeatured,
    this.minPrice,
    this.maxPrice,
    this.underMaintenance,
  });

  final String search;
  final int? cityId;
  final int? countryId;
  final int? surfaceTypeId;
  final int? courtTypeId;
  final bool? isIndoor;
  final bool? isActive;
  final bool? isFeatured;
  final double? minPrice;
  final double? maxPrice;
  final bool? underMaintenance;

  /// Rebuilds the filter set. Each parameter takes a sentinel-free pair so a
  /// caller can clear a value back to null (e.g. `cityId: null, clearCity: true`).
  CourtFilters copyWith({
    String? search,
    int? cityId,
    bool clearCity = false,
    int? countryId,
    bool clearCountry = false,
    int? surfaceTypeId,
    bool clearSurfaceType = false,
    int? courtTypeId,
    bool clearCourtType = false,
    bool? isIndoor,
    bool clearIndoor = false,
    bool? isActive,
    bool clearActive = false,
    bool? isFeatured,
    bool clearFeatured = false,
    double? minPrice,
    bool clearMinPrice = false,
    double? maxPrice,
    bool clearMaxPrice = false,
    bool? underMaintenance,
    bool clearUnderMaintenance = false,
  }) =>
      CourtFilters(
        search: search ?? this.search,
        cityId: clearCity ? null : (cityId ?? this.cityId),
        countryId: clearCountry ? null : (countryId ?? this.countryId),
        surfaceTypeId:
            clearSurfaceType ? null : (surfaceTypeId ?? this.surfaceTypeId),
        courtTypeId: clearCourtType ? null : (courtTypeId ?? this.courtTypeId),
        isIndoor: clearIndoor ? null : (isIndoor ?? this.isIndoor),
        isActive: clearActive ? null : (isActive ?? this.isActive),
        isFeatured: clearFeatured ? null : (isFeatured ?? this.isFeatured),
        minPrice: clearMinPrice ? null : (minPrice ?? this.minPrice),
        maxPrice: clearMaxPrice ? null : (maxPrice ?? this.maxPrice),
        underMaintenance: clearUnderMaintenance
            ? null
            : (underMaintenance ?? this.underMaintenance),
      );
}

/// Immutable state for the paginated, filterable court grid.
///
/// [value] holds the current page's [AsyncValue]; [page] and [filters] are the
/// inputs that produced it, kept outside the [AsyncValue] so they survive a
/// reload's transient loading state.
class CourtListState {
  const CourtListState({
    required this.value,
    this.page = 1,
    this.filters = const CourtFilters(),
  });

  final AsyncValue<PagedResult<Court>> value;
  final int page;
  final CourtFilters filters;

  CourtListState copyWith({
    AsyncValue<PagedResult<Court>>? value,
    int? page,
    CourtFilters? filters,
  }) =>
      CourtListState(
        value: value ?? this.value,
        page: page ?? this.page,
        filters: filters ?? this.filters,
      );
}

/// Drives the court grid: holds the filter + page state and re-fetches via the
/// repository. Mirrors the reference_data `ReferenceListController` pattern
/// (auto-load on build, guarded pagination, reload-after-create so the newest
/// court — backend orders `OrderByDescending(Id)` — appears on top).
class CourtListController extends Notifier<CourtListState> {
  @override
  CourtListState build() {
    Future.microtask(load);
    return const CourtListState(value: AsyncValue.loading());
  }

  Future<void> _fetchPage(int page, CourtFilters filters) async {
    state = state.copyWith(value: const AsyncValue.loading());
    final search = filters.search.trim();
    final value = await AsyncValue.guard(
      () => ref.read(courtRepositoryProvider).list(
            page: page,
            pageSize: kCourtPageSize,
            search: search.isEmpty ? null : search,
            cityId: filters.cityId,
            countryId: filters.countryId,
            surfaceTypeId: filters.surfaceTypeId,
            courtTypeId: filters.courtTypeId,
            isIndoor: filters.isIndoor,
            isActive: filters.isActive,
            isFeatured: filters.isFeatured,
            minPrice: filters.minPrice,
            maxPrice: filters.maxPrice,
            underMaintenance: filters.underMaintenance,
          ),
    );

    // Pager boundary clamp: if a delete empties the current page (e.g. the last
    // court on page 2), the backend returns an empty page even though courts
    // still exist on an earlier page. Step back and re-fetch so the grid never
    // shows "No courts found" while rows remain.
    final result = value.valueOrNull;
    if (result != null && result.items.isEmpty && page > 1) {
      return _fetchPage(page - 1, filters);
    }

    state = CourtListState(value: value, page: page, filters: filters);
  }

  /// Loads the current page with the current filters (first build + retry).
  Future<void> load() => _fetchPage(state.page, state.filters);

  /// Applies a new [search] term and reloads from page 1.
  Future<void> setSearch(String search) =>
      _fetchPage(1, state.filters.copyWith(search: search));

  /// Applies a new [filters] set and reloads from page 1.
  Future<void> setFilters(CourtFilters filters) => _fetchPage(1, filters);

  /// Clears every filter (keeps nothing) and reloads from page 1.
  Future<void> clearFilters() => _fetchPage(1, const CourtFilters());

  /// Advances one page (no-op when there is no next page).
  Future<void> nextPage() {
    final current = state.value.valueOrNull;
    if (current == null || !current.hasNext) return Future.value();
    return _fetchPage(state.page + 1, state.filters);
  }

  /// Goes back one page (no-op when already on the first page).
  Future<void> prevPage() {
    if (state.page <= 1) return Future.value();
    return _fetchPage(state.page - 1, state.filters);
  }

  /// Re-fetches the current page in place (after an update/delete).
  Future<void> refresh() => _fetchPage(state.page, state.filters);

  /// Resets to page 1 and reloads — used after a create so the newest court
  /// shows on top without any manual refresh.
  Future<void> reload() => _fetchPage(1, state.filters);
}

final courtListControllerProvider =
    NotifierProvider<CourtListController, CourtListState>(
        CourtListController.new);

/// A single court by id (F10 `GET /api/courts/{id}`), keyed by court id. Used by
/// the client booking screen (F25) for its read-only court header — one call,
/// versus reusing the heavier `courtDetailProvider` (which also loads amenities,
/// reviews and review-eligibility).
final courtByIdProvider = FutureProvider.family<Court, int>(
  (ref, courtId) => ref.watch(courtRepositoryProvider).getById(courtId),
);

/// The maintenance windows (status history) for a court, newest-first (F12). The
/// maintenance modal watches this; after a create/start/fix/cancel, invalidate
/// `maintenanceHistoryProvider(courtId)` to refresh the timeline.
final maintenanceHistoryProvider =
    FutureProvider.family<List<CourtMaintenanceLog>, int>((ref, courtId) async {
  final result =
      await ref.watch(courtRepositoryProvider).listMaintenance(courtId);
  return result.items;
});

/// One day's slot availability for a court (F13), keyed by court + (date-only)
/// day. The slots modal watches this for the selected date; after a generate or
/// remove, invalidate `slotAvailabilityProvider((courtId: id, date: day))` to
/// refresh the day without a manual reload. Callers must pass a date-only
/// [DateTime] (`DateTime(y, m, d)`) so the family key stays stable.
final slotAvailabilityProvider =
    FutureProvider.family<DayAvailability, ({int courtId, DateTime date})>(
  (ref, query) =>
      ref.watch(courtRepositoryProvider).availability(query.courtId, query.date),
);

// --- FK dropdown lookups -----------------------------------------------------
//
// No dedicated lookup endpoints exist for cities/surface-types/court-types
// (recon §6 — only countries has `/lookup`). We reuse the existing paged list
// endpoints via `referenceRepositoryProvider` with a large page so the form
// dropdowns are DB-driven without editing any F9 file.

/// Cities for the City [DbDropdown] / city filter (FK by name, never id).
final cityLookupProvider = FutureProvider<List<City>>(
  (ref) async => (await ref
          .watch(referenceRepositoryProvider)
          .listCities(pageSize: kLookupPageSize))
      .items,
);

/// Surface types for the SurfaceType [DbDropdown] + the surface filter chips.
final surfaceTypeLookupProvider = FutureProvider<List<SurfaceType>>(
  (ref) async => (await ref
          .watch(referenceRepositoryProvider)
          .listSurfaceTypes(pageSize: kLookupPageSize))
      .items,
);

/// Court types for the CourtType [DbDropdown] / court-type filter.
final courtTypeLookupProvider = FutureProvider<List<CourtType>>(
  (ref) async => (await ref
          .watch(referenceRepositoryProvider)
          .listCourtTypes(pageSize: kLookupPageSize))
      .items,
);

/// Amenities for the court form's amenity multi-select (F11). Mirrors the other
/// lookups — one large page via the existing reference list endpoint.
final amenityLookupProvider = FutureProvider<List<Amenity>>(
  (ref) async => (await ref
          .watch(referenceRepositoryProvider)
          .listAmenities(pageSize: kLookupPageSize))
      .items,
);

/// Countries for the country filter (reuses the cached `/lookup` endpoint).
final courtCountryLookupProvider = FutureProvider<List<Country>>(
  (ref) => ref.watch(referenceRepositoryProvider).lookupCountries(),
);

/// The FK lookups the create/edit form needs, loaded **together** with
/// [Future.wait] (recon — independent loads run concurrently). Feeds the City,
/// SurfaceType and CourtType dropdowns plus the F11 amenity multi-select from a
/// single [AsyncValue].
class CourtFormLookups {
  const CourtFormLookups({
    required this.cities,
    required this.surfaceTypes,
    required this.courtTypes,
    required this.amenities,
  });

  final List<City> cities;
  final List<SurfaceType> surfaceTypes;
  final List<CourtType> courtTypes;
  final List<Amenity> amenities;
}

final courtFormLookupsProvider = FutureProvider<CourtFormLookups>((ref) async {
  final repo = ref.watch(referenceRepositoryProvider);
  final results = await Future.wait([
    repo.listCities(pageSize: kLookupPageSize),
    repo.listSurfaceTypes(pageSize: kLookupPageSize),
    repo.listCourtTypes(pageSize: kLookupPageSize),
    repo.listAmenities(pageSize: kLookupPageSize),
  ]);
  return CourtFormLookups(
    cities: (results[0] as PagedResult<City>).items,
    surfaceTypes: (results[1] as PagedResult<SurfaceType>).items,
    courtTypes: (results[2] as PagedResult<CourtType>).items,
    amenities: (results[3] as PagedResult<Amenity>).items,
  );
});
