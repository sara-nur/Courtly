import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../features/notifications/application/notification_providers.dart';
import 'courtly_logo.dart';

/// Client mobile shell: a persistent bottom navigation bar (Home / Search /
/// Bookings / Notifications / Profile) wrapping the routed body, matching
/// `ui_design_and_scope.pdf` p.8. Sign-out lives on the Profile tab.
///
/// A [ConsumerWidget] so the Notifications tab can show a live unread badge
/// driven by the realtime controller (F27): reading only the count via
/// `.select` keeps the shell from rebuilding on unrelated realtime changes.
class ClientShell extends ConsumerWidget {
  const ClientShell({super.key, required this.navigationShell});

  /// Supplied by `StatefulShellRoute.indexedStack`; preserves each tab's state.
  final StatefulNavigationShell navigationShell;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final unread = ref.watch(
      notificationsRealtimeControllerProvider.select((s) => s.unreadCount),
    );

    return Scaffold(
      appBar: AppBar(title: const CourtlyLogo()),
      body: navigationShell,
      bottomNavigationBar: NavigationBar(
        selectedIndex: navigationShell.currentIndex,
        destinations: _destinations(unread),
        onDestinationSelected: (index) => navigationShell.goBranch(
          index,
          initialLocation: index == navigationShell.currentIndex,
        ),
      ),
    );
  }

  /// The five bottom-nav destinations. The Notifications entry wraps its bell in
  /// a [Badge.count] showing the live unread count (hidden when zero) — F27.
  List<NavigationDestination> _destinations(int unread) => [
        const NavigationDestination(
          icon: Icon(Icons.home_outlined),
          selectedIcon: Icon(Icons.home),
          label: 'Home',
        ),
        const NavigationDestination(
          icon: Icon(Icons.search_outlined),
          selectedIcon: Icon(Icons.search),
          label: 'Search',
        ),
        const NavigationDestination(
          icon: Icon(Icons.calendar_today_outlined),
          selectedIcon: Icon(Icons.calendar_today),
          label: 'Bookings',
        ),
        NavigationDestination(
          icon: Badge.count(
            count: unread,
            isLabelVisible: unread > 0,
            child: const Icon(Icons.notifications_none),
          ),
          selectedIcon: Badge.count(
            count: unread,
            isLabelVisible: unread > 0,
            child: const Icon(Icons.notifications),
          ),
          label: 'Notifications',
        ),
        const NavigationDestination(
          icon: Icon(Icons.person_outline),
          selectedIcon: Icon(Icons.person),
          label: 'Profile',
        ),
      ];
}
