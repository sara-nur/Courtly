import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../reference_data/domain/reference_models.dart' show PagedResult;
import '../domain/reservation_models.dart';
import 'reservation_providers.dart';

/// The two views of the client's Bookings tab (F26A): [upcoming] reservations
/// whose slot is still ahead, and [past] reservations whose slot has already
/// started. The split is by slot-start time (not status) so a booking moves from
/// Upcoming to Past exactly when its start passes — mirroring the mockup's
/// "upcoming + past" wording.
enum BookingsTab { upcoming, past }

/// Page size for the client Bookings list (single source — no magic numbers). A
/// touch smaller than the admin table's 20 for a comfortable mobile page.
const int kClientBookingsPageSize = 10;

/// Immutable state for the client Bookings tab: the current [tab], the current
/// [page], and the page's [value]. [tab] and [page] live outside the
/// [AsyncValue] so they survive a reload's transient loading state (mirrors
/// `ReservationListState`).
class ClientBookingsState {
  const ClientBookingsState({
    required this.value,
    this.tab = BookingsTab.upcoming,
    this.page = 1,
  });

  final AsyncValue<PagedResult<Reservation>> value;
  final BookingsTab tab;
  final int page;

  ClientBookingsState copyWith({
    AsyncValue<PagedResult<Reservation>>? value,
    BookingsTab? tab,
    int? page,
  }) =>
      ClientBookingsState(
        value: value ?? this.value,
        tab: tab ?? this.tab,
        page: page ?? this.page,
      );
}

/// Drives the client Bookings tab: fetches the signed-in customer's own
/// reservations (`GET /api/reservations/mine`, owner from the JWT) for the
/// selected [BookingsTab], paginated and server-ordered newest-first. The
/// Upcoming/Past split is pushed to the backend via the `fromUtc`/`toUtc` slot
/// bounds, so each tab is one indexed query — no client-side filtering of a full
/// list (rubric §8.2). Mirrors `ReservationListController` (auto-load on build,
/// guarded pagination, boundary clamp).
class ClientBookingsController extends Notifier<ClientBookingsState> {
  @override
  ClientBookingsState build() {
    Future.microtask(load);
    return const ClientBookingsState(value: AsyncValue.loading());
  }

  Future<void> _fetchPage(BookingsTab tab, int page) async {
    state = state.copyWith(value: const AsyncValue.loading(), tab: tab, page: page);

    // Partition by slot start at "now": Upcoming = start ≥ now (fromUtc),
    // Past = start < now (toUtc). Read the clock once per fetch.
    final now = DateTime.now().toUtc();
    final value = await AsyncValue.guard(
      () => ref.read(reservationRepositoryProvider).listMine(
            page: page,
            pageSize: kClientBookingsPageSize,
            fromUtc: tab == BookingsTab.upcoming ? now : null,
            toUtc: tab == BookingsTab.past ? now : null,
          ),
    );

    // Pager boundary clamp: if an action empties the current page, step back.
    final result = value.valueOrNull;
    if (result != null && result.items.isEmpty && page > 1) {
      return _fetchPage(tab, page - 1);
    }

    state = ClientBookingsState(value: value, tab: tab, page: page);
  }

  /// Loads the current tab's current page (first build + retry).
  Future<void> load() => _fetchPage(state.tab, state.page);

  /// Switches tab and loads its first page (no-op if already on [tab]).
  Future<void> setTab(BookingsTab tab) {
    if (tab == state.tab) return Future.value();
    return _fetchPage(tab, 1);
  }

  /// Advances one page (no-op when there is no next page).
  Future<void> nextPage() {
    final current = state.value.valueOrNull;
    if (current == null || !current.hasNext) return Future.value();
    return _fetchPage(state.tab, state.page + 1);
  }

  /// Goes back one page (no-op when already on the first page).
  Future<void> prevPage() {
    if (state.page <= 1) return Future.value();
    return _fetchPage(state.tab, state.page - 1);
  }

  /// Re-fetches the current tab + page in place (pull-to-refresh, or after a
  /// cancel so the row reflects its new status without a manual reload).
  Future<void> refresh() => _fetchPage(state.tab, state.page);
}

final clientBookingsControllerProvider =
    NotifierProvider<ClientBookingsController, ClientBookingsState>(
        ClientBookingsController.new);
