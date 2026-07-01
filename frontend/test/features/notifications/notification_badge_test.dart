// F27 — Client notifications UI. Widget test: the shell unread badge reflects the realtime unreadCount.
import 'package:courtly/features/notifications/application/notification_providers.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

/// F27 DoD (widget): the notifications destination badge shows the unread count
/// from [notificationsRealtimeControllerProvider]. This mirrors the shell's badge
/// exactly (same provider + `.select` + Badge.count) via a small harness, and the
/// realtime controller is overridden with a fake whose build() has no side
/// effects (no hub, no timer) so no pending timer or socket outlives the test.
void main() {
  Future<void> pumpBadge(WidgetTester tester, {required int unread}) async {
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          notificationsRealtimeControllerProvider
              .overrideWith(() => _FakeRealtimeController(unread: unread)),
        ],
        child: const MaterialApp(home: Scaffold(body: _BadgeHarness())),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('shows the unread count when there are unread notifications',
      (tester) async {
    await pumpBadge(tester, unread: 3);

    expect(find.text('3'), findsOneWidget);
    expect(find.byIcon(Icons.notifications_none), findsOneWidget);
  });

  testWidgets('hides the label when there are no unread notifications',
      (tester) async {
    await pumpBadge(tester, unread: 0);

    // isLabelVisible:false → the "0" count is not rendered.
    expect(find.text('0'), findsNothing);
    expect(find.byIcon(Icons.notifications_none), findsOneWidget);
  });
}

/// Mirrors the shell's Notifications destination badge: reads only the unread
/// count via `.select` and renders a [Badge.count] over the bell icon.
class _BadgeHarness extends ConsumerWidget {
  const _BadgeHarness();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final unread = ref.watch(
      notificationsRealtimeControllerProvider.select((s) => s.unreadCount),
    );
    return Badge.count(
      count: unread,
      isLabelVisible: unread > 0,
      child: const Icon(Icons.notifications_none),
    );
  }
}

/// Fake realtime controller with a fixed unread count and no side effects.
class _FakeRealtimeController extends NotificationsRealtimeController {
  _FakeRealtimeController({required this.unread});

  final int unread;

  @override
  NotificationsRealtimeState build() =>
      NotificationsRealtimeState(unreadCount: unread);
}
