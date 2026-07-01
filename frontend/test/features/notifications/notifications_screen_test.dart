// F27 — Client notifications UI. Widget test: list renders, mark-read + mark-all-read wire to the repo.
import 'package:courtly/core/enums/notification_type.dart';
import 'package:courtly/features/notifications/application/notification_providers.dart';
import 'package:courtly/features/notifications/data/notification_repository.dart';
import 'package:courtly/features/notifications/domain/notification_models.dart';
import 'package:courtly/features/notifications/presentation/notification_detail_screen.dart';
import 'package:courtly/features/notifications/presentation/notifications_screen.dart';
import 'package:courtly/features/reference_data/domain/reference_models.dart'
    show PagedResult;
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';

/// F27 DoD (widget): the notifications screen renders rows from the repository,
/// tapping an unread tile calls markRead(id), and the "Mark all read" app-bar
/// action calls markAllRead(). The realtime controller is overridden with a fake
/// whose build() has NO side effects (no hub, no timer) so the test never leaves
/// a pending timer or opens a socket.
void main() {
  AppNotification notification({
    required int id,
    required bool isRead,
    required String title,
    required String text,
  }) =>
      AppNotification(
        id: id,
        type: NotificationType.reservationConfirmed,
        title: title,
        text: text,
        isRead: isRead,
        createdAtUtc: DateTime.utc(2026, 7, 1, 8, 0),
        readAtUtc: isRead ? DateTime.utc(2026, 7, 1, 9, 0) : null,
      );

  Future<void> pumpScreen(
    WidgetTester tester, {
    required _FakeNotificationRepository repo,
    required int unread,
  }) async {
    tester.view.physicalSize = const Size(1200, 1600);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    // A GoRouter harness so the tile's `context.push` to the detail route
    // resolves (the list screen navigates on tap). The detail path mirrors
    // ClientRoutes.notificationDetail.
    final router = GoRouter(
      initialLocation: '/',
      routes: [
        GoRoute(
          path: '/',
          builder: (_, __) => const ClientNotificationsScreen(),
        ),
        GoRoute(
          path: '/notification-detail',
          builder: (_, state) => NotificationDetailScreen(
            notification: state.extra as AppNotification?,
          ),
        ),
      ],
    );

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          notificationRepositoryProvider.overrideWithValue(repo),
          notificationsRealtimeControllerProvider
              .overrideWith(() => _FakeRealtimeController(unread: unread)),
        ],
        child: MaterialApp.router(routerConfig: router),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('renders both notification rows from the repository',
      (tester) async {
    final repo = _FakeNotificationRepository([
      notification(
          id: 1, isRead: false, title: 'Booking confirmed', text: 'See you soon'),
      notification(
          id: 2, isRead: true, title: 'Payment received', text: 'Thanks!'),
    ]);

    await pumpScreen(tester, repo: repo, unread: 1);

    expect(find.text('Booking confirmed'), findsOneWidget);
    expect(find.text('Payment received'), findsOneWidget);
  });

  testWidgets('tapping the unread tile calls markRead with its id',
      (tester) async {
    final repo = _FakeNotificationRepository([
      notification(
          id: 11, isRead: false, title: 'Booking confirmed', text: 'See you soon'),
      notification(
          id: 22, isRead: true, title: 'Payment received', text: 'Thanks!'),
    ]);

    await pumpScreen(tester, repo: repo, unread: 1);

    await tester.tap(find.text('Booking confirmed'));
    await tester.pumpAndSettle();

    expect(repo.markReadIds, <int>[11]);
  });

  testWidgets('tapping a row opens the notification detail screen',
      (tester) async {
    final repo = _FakeNotificationRepository([
      notification(
          id: 5, isRead: true, title: 'Payment received', text: 'Thanks!'),
    ]);

    await pumpScreen(tester, repo: repo, unread: 0);

    await tester.tap(find.text('Payment received'));
    await tester.pumpAndSettle();

    // The detail screen is now on top — its app-bar title is the singular
    // 'Notification' (the list screen uses the plural 'Notifications').
    expect(find.text('Notification'), findsOneWidget);
  });

  testWidgets('the Mark all read app-bar action calls markAllRead',
      (tester) async {
    final repo = _FakeNotificationRepository([
      notification(
          id: 1, isRead: false, title: 'Booking confirmed', text: 'See you soon'),
      notification(
          id: 2, isRead: true, title: 'Payment received', text: 'Thanks!'),
    ]);

    await pumpScreen(tester, repo: repo, unread: 1);

    await tester.tap(find.byIcon(Icons.done_all));
    await tester.pumpAndSettle();

    expect(repo.markAllReadCalls, 1);
  });
}

/// Fake repository: `list` returns a single page of [rows]; markRead/markAllRead
/// record their calls. Any other member is an explicit test failure.
class _FakeNotificationRepository implements NotificationRepository {
  _FakeNotificationRepository(this.rows);

  final List<AppNotification> rows;
  final List<int> markReadIds = <int>[];
  int markAllReadCalls = 0;

  @override
  Future<PagedResult<AppNotification>> list({int page = 1, int pageSize = 20}) async =>
      PagedResult<AppNotification>(
        items: rows,
        page: 1,
        pageSize: pageSize,
        totalCount: rows.length,
        hasNext: false,
        hasPrevious: false,
      );

  @override
  Future<AppNotification> markRead(int id) async {
    markReadIds.add(id);
    return rows.firstWhere((n) => n.id == id).copyWith(isRead: true);
  }

  @override
  Future<void> markAllRead() async {
    markAllReadCalls++;
  }

  @override
  dynamic noSuchMethod(Invocation invocation) =>
      throw UnsupportedError('Unexpected call: ${invocation.memberName}');
}

/// Fake realtime controller: build() returns a fixed state with NO side effects
/// (no SignalR hub, no polling Timer), so the widget tree settles cleanly and no
/// pending timers or sockets survive the test. markRead/markAllRead delegate to
/// the overridden repository so the screen's wiring is exercised end-to-end.
class _FakeRealtimeController extends NotificationsRealtimeController {
  _FakeRealtimeController({required this.unread});

  final int unread;

  @override
  NotificationsRealtimeState build() =>
      NotificationsRealtimeState(unreadCount: unread);

  @override
  Future<void> markRead(int id) async {
    await ref.read(notificationRepositoryProvider).markRead(id);
  }

  @override
  Future<void> markAllRead() async {
    await ref.read(notificationRepositoryProvider).markAllRead();
  }
}
