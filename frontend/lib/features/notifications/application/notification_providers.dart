// F27 — Client notifications UI. Riverpod wiring + realtime controller (poll + SignalR hub).
import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/env/app_config.dart';
import '../../../core/network/dio_client.dart';
import '../../../core/network/token_storage.dart';
import '../../auth/application/auth_controller.dart';
import '../../reference_data/domain/reference_models.dart' show PagedResult;
import '../data/notification_api.dart';
import '../data/notification_hub_client.dart';
import '../data/notification_repository.dart';
import '../domain/notification_models.dart';

/// Transport over the notification endpoints, bound to the app Dio client
/// (mirrors `reviewApiProvider`).
final notificationApiProvider = Provider<NotificationApi>(
  (ref) => NotificationApi(ref.watch(dioProvider)),
);

/// The notification repository (mirrors `reviewRepositoryProvider`).
final notificationRepositoryProvider = Provider<NotificationRepository>(
  (ref) => NotificationRepository(ref.watch(notificationApiProvider)),
);

/// Page size for the notifications list screen (single source — no magic numbers).
const int kNotificationPageSize = 20;

/// How often the realtime controller re-checks the unread count and nudges the
/// visible list (a lightweight polling fallback for when the hub is down).
const Duration kNotificationPollInterval = Duration(seconds: 20);

/// One page of the signed-in user's notifications (newest-first), keyed by page.
/// The screen holds the current page locally and re-watches as it changes;
/// invalidate it after a mark-read / mark-all-read to reflect the change.
final notificationsPageProvider =
    FutureProvider.family<PagedResult<AppNotification>, int>(
  (ref, page) => ref
      .watch(notificationRepositoryProvider)
      .list(page: page, pageSize: kNotificationPageSize),
);

/// Immutable state for the realtime layer: drives the shell's unread badge and
/// bumps [revision] whenever anything changes so the visible list refreshes
/// (whether the change arrived via the hub push or the polling fallback).
class NotificationsRealtimeState {
  const NotificationsRealtimeState({
    this.unreadCount = 0,
    this.hubConnected = false,
    this.revision = 0,
  });

  final int unreadCount;
  final bool hubConnected;
  final int revision;

  NotificationsRealtimeState copyWith({
    int? unreadCount,
    bool? hubConnected,
    int? revision,
  }) =>
      NotificationsRealtimeState(
        unreadCount: unreadCount ?? this.unreadCount,
        hubConnected: hubConnected ?? this.hubConnected,
        revision: revision ?? this.revision,
      );
}

/// Builds a [NotificationHubClient]. Injected via a provider so tests can
/// override it with a no-op fake (no real socket in tests).
typedef NotificationHubClientFactory = NotificationHubClient Function({
  required String hubUrl,
  required AccessTokenFactory accessTokenFactory,
  required void Function(AppNotification) onNotification,
  void Function()? onConnected,
  void Function()? onDisconnected,
});

/// The default factory returns the real SignalR-backed client. Overridden in
/// tests with a fake so no socket is opened.
final notificationHubClientFactoryProvider =
    Provider<NotificationHubClientFactory>(
  (ref) => ({
    required hubUrl,
    required accessTokenFactory,
    required onNotification,
    onConnected,
    onDisconnected,
  }) =>
      SignalRNotificationHubClient(
        hubUrl: hubUrl,
        accessTokenFactory: accessTokenFactory,
        onNotification: onNotification,
        onConnected: onConnected,
        onDisconnected: onDisconnected,
      ),
);

/// Owns the realtime notification layer for the signed-in session: keeps the
/// unread count fresh via a hub push (real-time) with a polling fallback, and
/// bumps [NotificationsRealtimeState.revision] so the list screen refreshes.
///
/// Mirrors the `CourtListController` idiom (kick off side-effects off the build
/// frame with [Future.microtask], tear down in [ref.onDispose]). Only runs while
/// authenticated; the timer and hub are always released in teardown.
class NotificationsRealtimeController
    extends Notifier<NotificationsRealtimeState> {
  Timer? _pollTimer;
  NotificationHubClient? _hub;

  /// Guards deferred work against a teardown that happened while an async gap
  /// was open. The same Notifier instance is reused across auth-driven rebuilds,
  /// so a resumed `Future.microtask(_start)` / post-`await` state write must not
  /// install resources or touch a disposed notifier.
  bool _disposed = false;

  @override
  NotificationsRealtimeState build() {
    // Reset on every (re)build: the instance is reused across recomputes, so a
    // prior teardown's `_disposed = true` must be cleared here or a fresh start
    // after (re-)login would no-op.
    _disposed = false;
    final authed = ref.watch(authControllerProvider).isAuthenticated;
    ref.onDispose(() {
      _disposed = true;
      _teardown();
    });
    if (authed) {
      Future.microtask(_start);
    }
    return const NotificationsRealtimeState();
  }

  Future<void> _start() async {
    // Re-check on resume: a teardown or logout may have happened between the
    // microtask being scheduled and it running.
    if (_disposed || !ref.read(authControllerProvider).isAuthenticated) return;
    // Own the resources synchronously (before any await), cancelling/closing any
    // prior instances first so a re-entrant start can never orphan a timer/hub.
    _pollTimer?.cancel();
    _pollTimer = Timer.periodic(kNotificationPollInterval, (_) => _poll());
    _connectHub();
    await _refreshUnread();
  }

  Future<void> _refreshUnread() async {
    if (_disposed) return;
    try {
      final count = await ref.read(notificationRepositoryProvider).unreadCount();
      if (_disposed) return;
      state = state.copyWith(unreadCount: count);
    } catch (_) {
      // Swallow: the next poll retries. A transient failure must not crash the
      // shell badge.
    }
  }

  Future<void> _poll() async {
    if (_disposed) return;
    await _refreshUnread();
    if (_disposed) return;
    // Bump the revision so the visible list refreshes even when the hub is down
    // (polling fallback).
    state = state.copyWith(revision: state.revision + 1);
  }

  void _connectHub() {
    // Never overwrite a live hub without closing it first.
    _hub?.disconnect();
    final baseUrl = ref.read(appConfigProvider).apiBaseUrl;
    final hubUrl = '$baseUrl/hubs/notifications';
    final factory = ref.read(notificationHubClientFactoryProvider);
    _hub = factory(
      hubUrl: hubUrl,
      accessTokenFactory: () =>
          ref.read(tokenStorageProvider).readAccessToken(),
      onNotification: (_) => _onPush(),
      onConnected: () {
        if (!_disposed) state = state.copyWith(hubConnected: true);
      },
      onDisconnected: () {
        if (!_disposed) state = state.copyWith(hubConnected: false);
      },
    );
    _hub!.connect();
  }

  void _onPush() {
    if (_disposed) return;
    // A push arrived: signal the list to refresh and re-read the unread count.
    state = state.copyWith(revision: state.revision + 1);
    _refreshUnread();
  }

  /// Marks a single notification read, then refreshes the badge + list.
  Future<void> markRead(int id) async {
    await ref.read(notificationRepositoryProvider).markRead(id);
    if (_disposed) return;
    state = state.copyWith(revision: state.revision + 1);
    await _refreshUnread();
  }

  /// Marks every notification read (optimistically clears the badge), then
  /// signals the list to refresh.
  Future<void> markAllRead() async {
    await ref.read(notificationRepositoryProvider).markAllRead();
    if (_disposed) return;
    state = state.copyWith(unreadCount: 0, revision: state.revision + 1);
  }

  void _teardown() {
    _pollTimer?.cancel();
    _pollTimer = null;
    _hub?.disconnect();
    _hub = null;
  }
}

final notificationsRealtimeControllerProvider =
    NotifierProvider<NotificationsRealtimeController,
        NotificationsRealtimeState>(NotificationsRealtimeController.new);
