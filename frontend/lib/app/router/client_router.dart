import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../features/auth/application/auth_controller.dart';
import '../../features/auth/domain/auth_models.dart';
import '../../features/auth/presentation/auth_splash.dart';
import '../../features/auth/presentation/client_login_screen.dart';
import '../../features/auth/presentation/forgot_password_screen.dart';
import '../../features/auth/presentation/register_screen.dart';
import '../../features/client_booking/presentation/client_booking_screen.dart';
import '../../features/client_home/presentation/client_home_screen.dart';
import '../../features/court_detail/presentation/client_court_detail_screen.dart';
import '../../features/court_detail/presentation/client_court_reviews_screen.dart';
import '../../features/court_search/presentation/client_search_screen.dart';
import '../../features/news/domain/news_models.dart';
import '../../features/reservations/domain/reservation_models.dart';
import '../../features/news/presentation/client_news_detail_screen.dart';
import '../../features/payments/presentation/payment_screen.dart';
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

  /// Full-screen article reader, pushed above the bottom-nav shell from the
  /// Home news feed. The [News] article rides along as the route's `extra`.
  static const String newsDetail = '/news-detail';

  /// Court detail (F24), pushed above the shell from a Home/Search court card.
  /// The court id travels in the path so the screen loads fresh data (rating,
  /// reviews, eligibility). The nested `reviews` child is the full reviews list;
  /// the nested `booking` child is the F25 booking flow for that court.
  static String courtDetailPath(int courtId) => '/courts/$courtId';
  static String courtReviewsPath(int courtId) => '/courts/$courtId/reviews';
  static String bookingPath(int courtId) => '/courts/$courtId/booking';

  /// Payment + confirmation screen (F26), pushed after Confirm Booking creates
  /// the Pending reservation. The created reservation rides along as the route's
  /// `extra`; the screen shows the pay flow, then flips to the paid receipt.
  static const String payment = '/payment';
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
      // Full-screen article reader pushed above the shell from the Home feed.
      // The tapped article is passed as `extra` (the feed already has the full
      // text), so no by-id refetch is needed.
      GoRoute(
        path: ClientRoutes.newsDetail,
        builder: (context, state) =>
            ClientNewsDetailScreen(news: state.extra as News?),
      ),
      // Court detail + its reviews list, pushed full-screen above the shell (so
      // both get the standard back button). The court id comes from the path.
      GoRoute(
        path: '/courts/:id',
        builder: (context, state) => ClientCourtDetailScreen(
          courtId: int.parse(state.pathParameters['id']!),
        ),
        routes: [
          GoRoute(
            path: 'reviews',
            builder: (context, state) => ClientCourtReviewsScreen(
              courtId: int.parse(state.pathParameters['id']!),
              courtName: state.extra as String?,
            ),
          ),
          GoRoute(
            path: 'booking',
            builder: (context, state) => ClientBookingScreen(
              courtId: int.parse(state.pathParameters['id']!),
            ),
          ),
        ],
      ),
      // The F26 payment + confirmation screen, pushed after a successful create.
      // The created reservation is passed as `extra`; the screen runs the pay
      // flow and flips to the paid receipt (Add to Calendar / Return to Home).
      GoRoute(
        path: ClientRoutes.payment,
        builder: (context, state) =>
            PaymentScreen(reservation: state.extra as Reservation?),
      ),
      StatefulShellRoute.indexedStack(
        builder: (context, state, navigationShell) =>
            ClientShell(navigationShell: navigationShell),
        branches: [
          _branch(ClientRoutes.home, const ClientHomeScreen()),
          _branch(ClientRoutes.search, const ClientSearchScreen()),
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
