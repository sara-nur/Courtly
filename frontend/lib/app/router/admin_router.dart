import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../features/placeholders/feature_placeholder.dart';
import '../shell/admin_shell.dart';

/// Builds the admin desktop router. A `StatefulShellRoute.indexedStack` keeps
/// the top-nav persistent and preserves each tab's navigation state. Auth
/// guards are added in Feature 8; for now every branch shows a fixture screen.
GoRouter buildAdminRouter() {
  return GoRouter(
    initialLocation: '/dashboard',
    routes: [
      StatefulShellRoute.indexedStack(
        builder: (context, state, navigationShell) =>
            AdminShell(navigationShell: navigationShell),
        branches: [
          _branch(
            '/dashboard',
            const FeaturePlaceholder(
              title: 'Dashboard Overview',
              subtitle: "Welcome back, Admin. Here's what's happening today.",
              icon: Icons.dashboard_outlined,
            ),
          ),
          _branch(
            '/reservations',
            const FeaturePlaceholder(
              title: 'Reservation Management',
              subtitle: 'View and manage all court bookings.',
              icon: Icons.event_note_outlined,
            ),
          ),
          _branch(
            '/courts',
            const FeaturePlaceholder(
              title: 'Court Management',
              subtitle: 'Monitor status, update pricing and maintenance.',
              icon: Icons.sports_tennis_outlined,
            ),
          ),
          _branch(
            '/users',
            const FeaturePlaceholder(
              title: 'Users',
              subtitle: 'Browse customers and their reservations.',
              icon: Icons.people_outline,
            ),
          ),
          _branch(
            '/reports',
            const FeaturePlaceholder(
              title: 'Reports',
              subtitle: 'Generate downloadable, printable PDF reports.',
              icon: Icons.bar_chart_outlined,
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
