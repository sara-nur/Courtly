import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/enums/reservation_status.dart';
import '../../../core/network/dio_client.dart';
import '../../court_catalog/application/court_providers.dart';
import '../../court_catalog/domain/court_models.dart';
import '../data/reservation_api.dart';
import '../data/reservation_repository.dart';
import '../domain/reservation_models.dart';

/// Transport over the reservation endpoints, bound to the app Dio client.
final reservationApiProvider = Provider<ReservationApi>(
  (ref) => ReservationApi(ref.watch(dioProvider)),
);

/// The reservation repository (normalizes failures to `ApiException`).
final reservationRepositoryProvider = Provider<ReservationRepository>(
  (ref) => ReservationRepository(ref.watch(reservationApiProvider)),
);

/// Default page size for the reservation table (single source — no magic numbers).
const int kReservationPageSize = 20;

/// Page size for the client home feed's recent-reservations preview (single source).
const int kMyRecentReservationsSize = 5;

/// The client home feed: the signed-in customer's most recent reservations
/// (first page, newest-first per the backend), capped at
/// [kMyRecentReservationsSize].
final myRecentReservationsProvider = FutureProvider<PagedResult<Reservation>>(
  (ref) => ref.watch(reservationRepositoryProvider).listMine(
        page: 1,
        pageSize: kMyRecentReservationsSize,
      ),
);

/// Page size for the court lookup behind the Court filter + "+ New Booking".
const int kCourtLookupPageSize = 100;

/// Active courts for the Court filter [DbDropdown] and the "+ New Booking" court
/// picker (rendered by name, never id). Reuses the F10 court repository.
final reservationCourtLookupProvider = FutureProvider<List<Court>>((ref) async {
  final page = await ref
      .watch(courtRepositoryProvider)
      .list(pageSize: kCourtLookupPageSize, isActive: true);
  return page.items;
});

/// Immutable filter set for the reservation list. Every field is optional; a
/// null value means "don't filter on this".
class ReservationFilters {
  const ReservationFilters({
    this.status,
    this.courtId,
    this.userId,
    this.fromUtc,
    this.toUtc,
  });

  final ReservationStatus? status;
  final int? courtId;
  final String? userId;
  final DateTime? fromUtc;
  final DateTime? toUtc;

  /// Rebuilds the filter set. Each value takes a `clearXxx` flag so a caller can
  /// reset it back to null (mirrors `CourtFilters.copyWith`).
  ReservationFilters copyWith({
    ReservationStatus? status,
    bool clearStatus = false,
    int? courtId,
    bool clearCourt = false,
    String? userId,
    bool clearUser = false,
    DateTime? fromUtc,
    bool clearFrom = false,
    DateTime? toUtc,
    bool clearTo = false,
  }) =>
      ReservationFilters(
        status: clearStatus ? null : (status ?? this.status),
        courtId: clearCourt ? null : (courtId ?? this.courtId),
        userId: clearUser ? null : (userId ?? this.userId),
        fromUtc: clearFrom ? null : (fromUtc ?? this.fromUtc),
        toUtc: clearTo ? null : (toUtc ?? this.toUtc),
      );
}

/// Immutable state for the paginated, filterable reservation table. [value] is
/// the current page's [AsyncValue]; [page] and [filters] are kept outside it so
/// they survive a reload's transient loading state.
class ReservationListState {
  const ReservationListState({
    required this.value,
    this.page = 1,
    this.filters = const ReservationFilters(),
  });

  final AsyncValue<PagedResult<Reservation>> value;
  final int page;
  final ReservationFilters filters;

  ReservationListState copyWith({
    AsyncValue<PagedResult<Reservation>>? value,
    int? page,
    ReservationFilters? filters,
  }) =>
      ReservationListState(
        value: value ?? this.value,
        page: page ?? this.page,
        filters: filters ?? this.filters,
      );
}

/// Drives the reservation table: holds the filter + page state and re-fetches
/// via the repository. Mirrors `CourtListController` (auto-load on build, guarded
/// pagination, reload-after-create so the newest reservation — backend orders
/// newest-first — appears on top without a manual refresh).
class ReservationListController extends Notifier<ReservationListState> {
  @override
  ReservationListState build() {
    Future.microtask(load);
    return const ReservationListState(value: AsyncValue.loading());
  }

  Future<void> _fetchPage(int page, ReservationFilters filters) async {
    state = state.copyWith(value: const AsyncValue.loading());
    final value = await AsyncValue.guard(
      () => ref.read(reservationRepositoryProvider).list(
            page: page,
            pageSize: kReservationPageSize,
            status: filters.status,
            courtId: filters.courtId,
            userId: filters.userId,
            fromUtc: filters.fromUtc,
            toUtc: filters.toUtc,
          ),
    );

    // Pager boundary clamp: if an action empties the current page, step back.
    final result = value.valueOrNull;
    if (result != null && result.items.isEmpty && page > 1) {
      return _fetchPage(page - 1, filters);
    }

    state = ReservationListState(value: value, page: page, filters: filters);
  }

  /// Loads the current page with the current filters (first build + retry).
  Future<void> load() => _fetchPage(state.page, state.filters);

  /// Applies a new [filters] set and reloads from page 1.
  Future<void> setFilters(ReservationFilters filters) => _fetchPage(1, filters);

  /// Clears every filter and reloads from page 1.
  Future<void> clearFilters() => _fetchPage(1, const ReservationFilters());

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

  /// Re-fetches the current page in place (after a transition/reschedule).
  Future<void> refresh() => _fetchPage(state.page, state.filters);

  /// Resets to page 1 and reloads — used after a create so the newest booking
  /// shows on top without any manual refresh.
  Future<void> reload() => _fetchPage(1, state.filters);
}

final reservationListControllerProvider =
    NotifierProvider<ReservationListController, ReservationListState>(
        ReservationListController.new);

/// One reservation's full detail (reservation + audit trail + payment), keyed by
/// id. The detail screen watches this; after a transition/reschedule, invalidate
/// `reservationDetailProvider(id)` to refresh without a manual reload.
final reservationDetailProvider =
    FutureProvider.family<ReservationDetail, int>((ref, id) async {
  return ref.watch(reservationRepositoryProvider).getById(id);
});
