import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import 'courtly_logo.dart';

/// Client mobile shell: a persistent bottom navigation bar (Home / Search /
/// Bookings / Notifications / Profile) wrapping the routed body, matching
/// `ui_design_and_scope.pdf` p.8. Sign-out lives on the Profile tab.
class ClientShell extends StatelessWidget {
  const ClientShell({super.key, required this.navigationShell});

  /// Supplied by `StatefulShellRoute.indexedStack`; preserves each tab's state.
  final StatefulNavigationShell navigationShell;

  static const List<NavigationDestination> _destinations = [
    NavigationDestination(
      icon: Icon(Icons.home_outlined),
      selectedIcon: Icon(Icons.home),
      label: 'Home',
    ),
    NavigationDestination(
      icon: Icon(Icons.search_outlined),
      selectedIcon: Icon(Icons.search),
      label: 'Search',
    ),
    NavigationDestination(
      icon: Icon(Icons.calendar_today_outlined),
      selectedIcon: Icon(Icons.calendar_today),
      label: 'Bookings',
    ),
    NavigationDestination(
      icon: Icon(Icons.notifications_none),
      selectedIcon: Icon(Icons.notifications),
      label: 'Notifications',
    ),
    NavigationDestination(
      icon: Icon(Icons.person_outline),
      selectedIcon: Icon(Icons.person),
      label: 'Profile',
    ),
  ];

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const CourtlyLogo()),
      body: navigationShell,
      bottomNavigationBar: NavigationBar(
        selectedIndex: navigationShell.currentIndex,
        destinations: _destinations,
        onDestinationSelected: (index) => navigationShell.goBranch(
          index,
          initialLocation: index == navigationShell.currentIndex,
        ),
      ),
    );
  }
}
