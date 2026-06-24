import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../features/auth/application/auth_controller.dart';
import '../../features/auth/domain/auth_models.dart';
import '../../features/auth/presentation/auth_splash.dart';
import '../../features/auth/presentation/login_screen.dart';
import '../../features/placeholders/feature_placeholder.dart';
import '../../features/reference_data/presentation/settings_screen.dart';
import '../shell/admin_shell.dart';

/// Admin route paths (single source — rubric §3.4: no scattered string literals).
abstract final class AdminRoutes {
  static const String splash = '/splash';
  static const String login = '/login';
  static const String dashboard = '/dashboard';
  static const String reservations = '/reservations';
  static const String courts = '/courts';
  static const String users = '/users';
  static const String reports = '/reports';
  static const String settings = '/settings';
}

/// Builds the admin desktop router with an auth guard. The redirect reads the
/// [AuthController] state and a [ValueNotifier] (bumped on every auth change via
/// `ref.listen`) drives `refreshListenable`, so login/logout re-evaluate routing
/// immediately. Authenticated routes live under the persistent top-nav shell.
final adminRouterProvider = Provider<GoRouter>((ref) {
  final refreshSignal = ValueNotifier<int>(0);
  ref.onDispose(refreshSignal.dispose);
  ref.listen(authControllerProvider, (_, __) => refreshSignal.value++);

  return GoRouter(
    initialLocation: AdminRoutes.dashboard,
    refreshListenable: refreshSignal,
    redirect: (context, state) {
      final status = ref.read(authControllerProvider).status;
      final location = state.matchedLocation;
      final atSplash = location == AdminRoutes.splash;
      final atLogin = location == AdminRoutes.login;

      switch (status) {
        case AuthStatus.unknown:
          // Still validating a stored session — hold on the splash.
          return atSplash ? null : AdminRoutes.splash;
        case AuthStatus.unauthenticated:
          return atLogin ? null : AdminRoutes.login;
        case AuthStatus.authenticated:
          // Bounce away from the auth-only screens once signed in.
          return (atLogin || atSplash) ? AdminRoutes.dashboard : null;
      }
    },
    routes: [
      GoRoute(
        path: AdminRoutes.splash,
        builder: (context, state) => const AuthSplashScreen(),
      ),
      GoRoute(
        path: AdminRoutes.login,
        builder: (context, state) => const AdminLoginScreen(),
      ),
      StatefulShellRoute.indexedStack(
        builder: (context, state, navigationShell) =>
            AdminShell(navigationShell: navigationShell),
        branches: [
          _branch(
            AdminRoutes.dashboard,
            const FeaturePlaceholder(
              title: 'Dashboard Overview',
              subtitle: "Welcome back, Admin. Here's what's happening today.",
              icon: Icons.dashboard_outlined,
            ),
          ),
          _branch(
            AdminRoutes.reservations,
            const FeaturePlaceholder(
              title: 'Reservation Management',
              subtitle: 'View and manage all court bookings.',
              icon: Icons.event_note_outlined,
            ),
          ),
          _branch(
            AdminRoutes.courts,
            const FeaturePlaceholder(
              title: 'Court Management',
              subtitle: 'Monitor status, update pricing and maintenance.',
              icon: Icons.sports_tennis_outlined,
            ),
          ),
          _branch(
            AdminRoutes.users,
            const FeaturePlaceholder(
              title: 'Users',
              subtitle: 'Browse customers and their reservations.',
              icon: Icons.people_outline,
            ),
          ),
          _branch(
            AdminRoutes.reports,
            const FeaturePlaceholder(
              title: 'Reports',
              subtitle: 'Generate downloadable, printable PDF reports.',
              icon: Icons.bar_chart_outlined,
            ),
          ),
          _branch(AdminRoutes.settings, const SettingsScreen()),
        ],
      ),
    ],
  );
});

StatefulShellBranch _branch(String path, Widget child) {
  return StatefulShellBranch(
    routes: [
      GoRoute(path: path, builder: (context, state) => child),
    ],
  );
}
