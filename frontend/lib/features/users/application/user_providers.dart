import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/network/dio_client.dart';
import '../../reference_data/domain/reference_models.dart' show PagedResult;
import '../data/user_api.dart';
import '../data/user_repository.dart';
import '../domain/user_detail.dart';
import '../domain/user_summary.dart';

/// Transport over the user endpoint, bound to the app Dio client.
final userApiProvider = Provider<UserApi>(
  (ref) => UserApi(ref.watch(dioProvider)),
);

/// The user repository (normalizes failures to `ApiException`).
final userRepositoryProvider = Provider<UserRepository>(
  (ref) => UserRepository(ref.watch(userApiProvider)),
);

/// Page size for the customer lookup behind "+ New Booking". One large page is
/// the practical lookup (the picker is searchable; this caps the initial load).
const int kUserLookupPageSize = 100;

/// Active customers for the "+ New Booking" customer [DbDropdown] (name + email,
/// never a raw id). Bookings can only be created for an active user, so the
/// picker lists active users only.
final userLookupProvider = FutureProvider<List<UserSummary>>((ref) async {
  final page = await ref
      .watch(userRepositoryProvider)
      .search(pageSize: kUserLookupPageSize);
  return page.items.where((u) => u.isActive).toList(growable: false);
});

/// Default page size for the users table (single source — no magic numbers).
const int kUserPageSize = 20;

/// Immutable state for the paginated, searchable users table. [value] is the
/// current page's [AsyncValue]; [page] and [search] are kept outside it so they
/// survive a reload's transient loading state. Mirrors `ReservationListState`.
class UserListState {
  const UserListState({
    required this.value,
    this.page = 1,
    this.search,
  });

  final AsyncValue<PagedResult<UserSummary>> value;
  final int page;
  final String? search;

  UserListState copyWith({
    AsyncValue<PagedResult<UserSummary>>? value,
    int? page,
    String? search,
    bool clearSearch = false,
  }) =>
      UserListState(
        value: value ?? this.value,
        page: page ?? this.page,
        search: clearSearch ? null : (search ?? this.search),
      );
}

/// Drives the users table: holds the search + page state and re-fetches via the
/// repository. Mirrors `ReservationListController` (auto-load on build, guarded
/// pagination, pager boundary clamp so an action that empties the current page
/// steps back).
class UserListController extends Notifier<UserListState> {
  @override
  UserListState build() {
    Future.microtask(load);
    return const UserListState(value: AsyncValue.loading());
  }

  Future<void> _fetchPage(int page, String? search) async {
    state = state.copyWith(value: const AsyncValue.loading());
    final value = await AsyncValue.guard(
      () => ref.read(userRepositoryProvider).search(
            page: page,
            pageSize: kUserPageSize,
            search: search,
          ),
    );

    // Pager boundary clamp: if an action empties the current page, step back.
    final result = value.valueOrNull;
    if (result != null && result.items.isEmpty && page > 1) {
      return _fetchPage(page - 1, search);
    }

    state = UserListState(value: value, page: page, search: search);
  }

  /// Loads the current page with the current search term (first build + retry).
  Future<void> load() => _fetchPage(state.page, state.search);

  /// Applies a new [search] term and reloads from page 1. An empty/blank term
  /// clears the filter.
  Future<void> setSearch(String? search) {
    final term = (search == null || search.trim().isEmpty) ? null : search.trim();
    return _fetchPage(1, term);
  }

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

  /// Re-fetches the current page in place (after an activate/role change).
  Future<void> refresh() => _fetchPage(state.page, state.search);

  /// Resets to page 1 and reloads.
  Future<void> reload() => _fetchPage(1, state.search);
}

final userListControllerProvider =
    NotifierProvider<UserListController, UserListState>(UserListController.new);

/// One user's full detail (profile + roles + active state), keyed by id. The
/// detail screen watches this; after an activate/deactivate or role change,
/// invalidate `userDetailProvider(id)` to refresh without a manual reload.
final userDetailProvider =
    FutureProvider.family<UserDetail, String>((ref, id) async {
  return ref.watch(userRepositoryProvider).getById(id);
});
