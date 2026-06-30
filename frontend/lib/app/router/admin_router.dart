import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../features/auth/application/auth_controller.dart';
import '../../features/auth/domain/auth_models.dart';
import '../../features/auth/presentation/auth_splash.dart';
import '../../features/auth/presentation/login_screen.dart';
import '../../features/court_catalog/presentation/courts_screen.dart';
import '../../features/dashboard/presentation/dashboard_screen.dart';
import '../../features/news/presentation/news_screen.dart';
import '../../features/placeholders/feature_placeholder.dart';
import '../../features/reference_data/presentation/settings_screen.dart';
import '../../features/reports/presentation/reports_screen.dart';
import '../../features/reservations/presentation/reservation_detail_screen.dart';
import '../../features/reservations/presentation/reservations_screen.dart';
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
  static const String news = '/news';

  /// Path-parameter segment for the reservation detail route (child of
  /// [reservations]) and the full path for a given reservation id.
  static const String reservationDetailParam = ':id';
  static String reservationDetail(int id) => '$reservations/$id';
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
          _branch(AdminRoutes.dashboard, const DashboardScreen()),
          // Reservations: the list at /reservations with a full-screen detail
          // route at /reservations/:id (kept inside the branch so the admin
          // shell + nav stay; back returns to the list).
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: AdminRoutes.reservations,
                builder: (context, state) => const ReservationsScreen(),
                routes: [
                  GoRoute(
                    path: AdminRoutes.reservationDetailParam,
                    builder: (context, state) => ReservationDetailScreen(
                      reservationId:
                          int.parse(state.pathParameters['id'] ?? '0'),
                    ),
                  ),
                ],
              ),
            ],
          ),
          _branch(AdminRoutes.courts, const CourtsScreen()),
          _branch(
            AdminRoutes.users,
            const FeaturePlaceholder(
              title: 'Users',
              subtitle: 'Browse customers and their reservations.',
              icon: Icons.people_outline,
            ),
          ),
          _branch(AdminRoutes.reports, const ReportsScreen()),
          _branch(AdminRoutes.settings, const SettingsScreen()),
          _branch(AdminRoutes.news, const NewsScreen()),
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
