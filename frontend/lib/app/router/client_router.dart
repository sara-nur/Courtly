import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../features/placeholders/feature_placeholder.dart';
import '../shell/client_shell.dart';

/// Builds the client mobile router. A `StatefulShellRoute.indexedStack` keeps
/// the bottom nav persistent and preserves each tab's navigation state. Auth
/// guards are added in Feature 8; for now every branch shows a fixture screen.
GoRouter buildClientRouter() {
  return GoRouter(
    initialLocation: '/home',
    routes: [
      StatefulShellRoute.indexedStack(
        builder: (context, state, navigationShell) =>
            ClientShell(navigationShell: navigationShell),
        branches: [
          _branch(
            '/home',
            const FeaturePlaceholder(
              title: 'Ready to serve?',
              subtitle: 'Find and book a court near you.',
              icon: Icons.home_outlined,
            ),
          ),
          _branch(
            '/search',
            const FeaturePlaceholder(
              title: 'Search',
              subtitle: 'Filter courts by price, surface and rating.',
              icon: Icons.search,
            ),
          ),
          _branch(
            '/bookings',
            const FeaturePlaceholder(
              title: 'My Bookings',
              subtitle: 'Upcoming and past reservations.',
              icon: Icons.calendar_today_outlined,
            ),
          ),
          _branch(
            '/notifications',
            const FeaturePlaceholder(
              title: 'Notifications',
              subtitle: 'Updates about your reservations.',
              icon: Icons.notifications_none,
            ),
          ),
          _branch(
            '/profile',
            const FeaturePlaceholder(
              title: 'Profile',
              subtitle: 'View and edit your account.',
              icon: Icons.person_outline,
            ),
          ),
        ],
      ),
    ],
  );
}

StatefulShellBranch _branch(String path, Widget child) {
  return StatefulShellBranch(
    routes: [
      GoRoute(path: path, builder: (context, state) => child),
    ],
  );
}
