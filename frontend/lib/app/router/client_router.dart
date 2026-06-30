import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../features/auth/application/auth_controller.dart';
import '../../features/auth/domain/auth_models.dart';
import '../../features/auth/presentation/auth_splash.dart';
import '../../features/auth/presentation/client_login_screen.dart';
import '../../features/auth/presentation/forgot_password_screen.dart';
import '../../features/auth/presentation/register_screen.dart';
import '../../features/placeholders/feature_placeholder.dart';
import '../../features/profile/presentation/client_profile_screen.dart';
import '../shell/client_shell.dart';

/// Client route paths (single source — rubric §3.4: no scattered string literals).
abstract final class ClientRoutes {
  static const String splash = '/splash';
  static const String login = '/login';
  static const String register = '/register';
  static const String forgotPassword = '/forgot-password';
  static const String home = '/home';
  static const String search = '/search';
  static const String bookings = '/bookings';
  static const String notifications = '/notifications';
  static const String profile = '/profile';
}

/// Builds the client mobile router with an auth guard, mirroring the admin router
/// (feature 8): a [ValueNotifier] bumped on every auth change via `ref.listen`
/// drives `refreshListenable`, so login/logout re-evaluate routing immediately.
/// Authenticated routes live under the persistent bottom-nav shell; the auth
/// screens (splash/login/register/forgot) sit before it. Password reset itself
/// happens on the web page the emailed link opens (served by the API), so the
/// client only needs the forgot-password request screen.
final clientRouterProvider = Provider<GoRouter>((ref) {
  final refreshSignal = ValueNotifier<int>(0);
  ref.onDispose(refreshSignal.dispose);
  ref.listen(authControllerProvider, (_, __) => refreshSignal.value++);

  return GoRouter(
    initialLocation: ClientRoutes.home,
    refreshListenable: refreshSignal,
    redirect: (context, state) {
      final status = ref.read(authControllerProvider).status;
      final location = state.matchedLocation;

      final onAuthScreen = location == ClientRoutes.login ||
          location == ClientRoutes.register ||
          location == ClientRoutes.forgotPassword;

      switch (status) {
        case AuthStatus.unknown:
          // Still validating a stored session — hold on the splash.
          return location == ClientRoutes.splash ? null : ClientRoutes.splash;
        case AuthStatus.unauthenticated:
          return onAuthScreen ? null : ClientRoutes.login;
        case AuthStatus.authenticated:
          // Bounce away from the auth-only screens once signed in.
          return (onAuthScreen || location == ClientRoutes.splash)
              ? ClientRoutes.home
              : null;
      }
    },
    routes: [
      GoRoute(
        path: ClientRoutes.splash,
        builder: (context, state) =>
            const AuthSplashScreen(showAdminSuffix: false),
      ),
      GoRoute(
        path: ClientRoutes.login,
        builder: (context, state) => const ClientLoginScreen(),
      ),
      GoRoute(
        path: ClientRoutes.register,
        builder: (context, state) => const RegisterScreen(),
      ),
      GoRoute(
        path: ClientRoutes.forgotPassword,
        builder: (context, state) => const ForgotPasswordScreen(),
      ),
      StatefulShellRoute.indexedStack(
        builder: (context, state, navigationShell) =>
            ClientShell(navigationShell: navigationShell),
        branches: [
          _branch(
            ClientRoutes.home,
            const FeaturePlaceholder(
              title: 'Ready to serve?',
              subtitle: 'Find and book a court near you.',
              icon: Icons.home_outlined,
            ),
          ),
          _branch(
            ClientRoutes.search,
            const FeaturePlaceholder(
              title: 'Search',
              subtitle: 'Filter courts by price, surface and rating.',
              icon: Icons.search,
            ),
          ),
          _branch(
            ClientRoutes.bookings,
            const FeaturePlaceholder(
              title: 'My Bookings',
              subtitle: 'Upcoming and past reservations.',
              icon: Icons.calendar_today_outlined,
            ),
          ),
          _branch(
            ClientRoutes.notifications,
            const FeaturePlaceholder(
              title: 'Notifications',
              subtitle: 'Updates about your reservations.',
              icon: Icons.notifications_none,
            ),
          ),
          _branch(ClientRoutes.profile, const ClientProfileScreen()),
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
