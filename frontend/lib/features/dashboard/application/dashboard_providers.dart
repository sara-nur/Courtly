import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/network/dio_client.dart';
import '../data/dashboard_api.dart';
import '../data/dashboard_repository.dart';
import '../domain/dashboard_models.dart';

/// Transport over the dashboard endpoint, bound to the app Dio client.
final dashboardApiProvider = Provider<DashboardApi>(
  (ref) => DashboardApi(ref.watch(dioProvider)),
);

/// The dashboard repository (normalizes failures to `ApiException`).
final dashboardRepositoryProvider = Provider<DashboardRepository>(
  (ref) => DashboardRepository(ref.watch(dashboardApiProvider)),
);

/// Auto-refresh cadence (single source — no magic numbers). Matches the backend
/// cache TTL so a tick reads the cached aggregate rather than recomputing it.
const Duration kDashboardRefreshInterval = Duration(seconds: 30);

/// Immutable filter set for the dashboard. All optional; null means "don't
/// filter" (an omitted date range defaults to the last 30 days on the server).
class DashboardFilters {
  const DashboardFilters({this.fromUtc, this.toUtc, this.courtTypeId});

  final DateTime? fromUtc;
  final DateTime? toUtc;
  final int? courtTypeId;

  /// True when the user has narrowed the default window/type at all.
  bool get isActive => fromUtc != null || toUtc != null || courtTypeId != null;

  DashboardFilters copyWith({
    DateTime? fromUtc,
    bool clearFrom = false,
    DateTime? toUtc,
    bool clearTo = false,
    int? courtTypeId,
    bool clearCourtType = false,
  }) =>
      DashboardFilters(
        fromUtc: clearFrom ? null : (fromUtc ?? this.fromUtc),
        toUtc: clearTo ? null : (toUtc ?? this.toUtc),
        courtTypeId: clearCourtType ? null : (courtTypeId ?? this.courtTypeId),
      );
}

/// Immutable state for the dashboard: the metrics [value] plus the current
/// [filters] (kept outside the AsyncValue so they survive a reload).
class DashboardState {
  const DashboardState({required this.value, this.filters = const DashboardFilters()});

  final AsyncValue<DashboardMetrics> value;
  final DashboardFilters filters;

  DashboardState copyWith({
    AsyncValue<DashboardMetrics>? value,
    DashboardFilters? filters,
  }) =>
      DashboardState(value: value ?? this.value, filters: filters ?? this.filters);
}

/// Drives the dashboard: holds the filter state, fetches the composite metrics,
/// and ticks an auto-refresh on [kDashboardRefreshInterval] (rubric §7.2 — no
/// manual refresh). The tick is SILENT: it never flips the UI back to a spinner
/// and keeps the last good data if a refresh fails, so the screen doesn't flicker.
class DashboardController extends Notifier<DashboardState> {
  @override
  DashboardState build() {
    final timer = Timer.periodic(kDashboardRefreshInterval, (_) => refresh());
    ref.onDispose(timer.cancel);
    Future.microtask(load);
    return const DashboardState(value: AsyncValue.loading());
  }

  Future<void> _fetch({required bool showLoading}) async {
    if (showLoading) {
      state = state.copyWith(value: const AsyncValue.loading());
    }

    final filters = state.filters;
    final value = await AsyncValue.guard(
      () => ref.read(dashboardRepositoryProvider).getMetrics(
            fromUtc: filters.fromUtc,
            toUtc: filters.toUtc,
            courtTypeId: filters.courtTypeId,
          ),
    );

    // A silent (auto-refresh) tick that failed keeps the last good snapshot.
    if (!showLoading && value.hasError && state.value.hasValue) {
      return;
    }

    state = state.copyWith(value: value);
  }

  /// Loads with the current filters (first build, retry).
  Future<void> load() => _fetch(showLoading: true);

  /// Applies a new [filters] set and reloads.
  Future<void> setFilters(DashboardFilters filters) {
    state = state.copyWith(filters: filters);
    return _fetch(showLoading: true);
  }

  /// Clears every filter (back to the default last-30-days window) and reloads.
  Future<void> clearFilters() {
    state = state.copyWith(filters: const DashboardFilters());
    return _fetch(showLoading: true);
  }

  /// Silent in-place refresh used by the auto-refresh timer.
  Future<void> refresh() => _fetch(showLoading: false);
}

final dashboardControllerProvider =
    NotifierProvider<DashboardController, DashboardState>(DashboardController.new);
