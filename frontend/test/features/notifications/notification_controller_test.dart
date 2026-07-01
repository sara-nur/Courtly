// F27 — Client notifications UI. Unit test for the realtime controller's
// mark-read / mark-all-read logic: they call the repository, re-read the unread
// count and bump `revision` (the signal the list screen listens to for an
// in-place refresh). This drives the REAL NotificationsRealtimeController with a
// fake repository and an *unauthenticated* auth state, so build() starts no poll
// Timer and opens no SignalR socket — the hub + polling transport are
// integration concerns exercised against the live stack (mirrors F18's stance),
// not here.
import 'package:courtly/core/enums/notification_type.dart';
import 'package:courtly/features/auth/application/auth_controller.dart';
import 'package:courtly/features/auth/domain/auth_models.dart';
import 'package:courtly/features/notifications/application/notification_providers.dart';
import 'package:courtly/features/notifications/data/notification_repository.dart';
import 'package:courtly/features/notifications/domain/notification_models.dart';
import 'package:courtly/features/reference_data/domain/reference_models.dart'
    show PagedResult;
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  late _FakeNotificationRepository repo;

  ProviderContainer makeContainer() {
    final container = ProviderContainer(
      overrides: [
        notificationRepositoryProvider.overrideWithValue(repo),
        // Unauthenticated -> build() starts no timer/hub, so the test is
        // deterministic and leaves no pending timers. The mark actions run
        // independently of the auth-gated start path.
        authControllerProvider.overrideWith(_FakeAuthController.new),
      ],
    );
    addTearDown(container.dispose);
    return container;
  }

  setUp(() => repo = _FakeNotificationRepository());

  test('starts empty with no timer/hub while unauthenticated', () {
    final container = makeContainer();
    final state = container.read(notificationsRealtimeControllerProvider);
    expect(state.unreadCount, 0);
    expect(state.revision, 0);
    expect(state.hubConnected, isFalse);
  });

  test('markRead calls the repo, re-reads the unread count and bumps revision',
      () async {
    repo.unread = 4;
    final container = makeContainer();
    final controller =
        container.read(notificationsRealtimeControllerProvider.notifier);

    await controller.markRead(7);

    expect(repo.markReadIds, <int>[7]);
    final state = container.read(notificationsRealtimeControllerProvider);
    expect(state.unreadCount, 4); // re-read from the server, not guessed
    expect(state.revision, greaterThan(0)); // list-refresh signal fired
  });

  test('markAllRead clears the unread count and bumps revision', () async {
    repo.unread = 4;
    final container = makeContainer();
    final controller =
        container.read(notificationsRealtimeControllerProvider.notifier);

    await controller.markRead(1); // revision -> 1, unreadCount -> 4
    final before = container.read(notificationsRealtimeControllerProvider);

    await controller.markAllRead();

    expect(repo.markAllReadCalls, 1);
    final after = container.read(notificationsRealtimeControllerProvider);
    expect(after.unreadCount, 0);
    expect(after.revision, greaterThan(before.revision));
  });
}

/// Records mark calls and serves a controllable unread count. `list` returns an
/// empty page (unused by these tests); any other member is an explicit failure.
class _FakeNotificationRepository implements NotificationRepository {
  int unread = 0;
  final List<int> markReadIds = <int>[];
  int markAllReadCalls = 0;

  @override
  Future<int> unreadCount() async => unread;

  @override
  Future<AppNotification> markRead(int id) async {
    markReadIds.add(id);
    return AppNotification(
      id: id,
      type: NotificationType.reservationConfirmed,
      title: 'Booking confirmed',
      text: 'Marked read',
      isRead: true,
      createdAtUtc: DateTime.utc(2026, 7, 1, 8),
      readAtUtc: DateTime.utc(2026, 7, 1, 9),
    );
  }

  @override
  Future<void> markAllRead() async => markAllReadCalls++;

  @override
  Future<PagedResult<AppNotification>> list({
    int page = 1,
    int pageSize = 20,
  }) async =>
      const PagedResult<AppNotification>(
        items: <AppNotification>[],
        page: 1,
        pageSize: 20,
        totalCount: 0,
        hasNext: false,
        hasPrevious: false,
      );

  @override
  dynamic noSuchMethod(Invocation invocation) =>
      throw UnsupportedError('Unexpected call: ${invocation.memberName}');
}

/// Fake auth controller: build() returns an unauthenticated state with no side
/// effects (the real one touches secure storage / session-restore).
class _FakeAuthController extends AuthController {
  @override
  AuthState build() => const AuthState.unauthenticated();
}
