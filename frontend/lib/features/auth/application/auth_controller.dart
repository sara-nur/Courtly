import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/network/dio_client.dart';
import '../domain/auth_models.dart';
import 'auth_providers.dart';

/// Holds the [AuthState] for the whole app and is the only place the UI mutates
/// the session. On creation it restores any stored session (staying [AuthStatus.unknown]
/// until that resolves) and subscribes to the interceptor's session-expiry
/// signal so a failed background refresh logs the user out and triggers the
/// router redirect.
class AuthController extends Notifier<AuthState> {
  @override
  AuthState build() {
    final signal = ref.read(sessionExpiredSignalProvider);
    signal.addListener(_onSessionExpired);
    ref.onDispose(() => signal.removeListener(_onSessionExpired));

    // Validate any stored session off the build frame; UI shows the splash
    // while status is `unknown`.
    Future.microtask(_restore);
    return const AuthState.unknown();
  }

  Future<void> _restore() async {
    final user = await ref.read(authRepositoryProvider).restore();
    state = user == null
        ? const AuthState.unauthenticated()
        : AuthState.authenticated(user);
  }

  void _onSessionExpired() {
    if (state.isAuthenticated) {
      state = const AuthState.unauthenticated();
    }
  }

  /// Authenticates with the API and enforces the role gate. On success the state
  /// becomes authenticated (the router redirects to the dashboard). On failure
  /// the [ApiException] propagates so the form can show it below the fields or
  /// as a banner.
  Future<void> login(String userNameOrEmail, String password) async {
    final user =
        await ref.read(authRepositoryProvider).login(userNameOrEmail, password);
    state = AuthState.authenticated(user);
  }

  /// Registers a new account and signs it in (the server auto-logs-in). On
  /// success the state becomes authenticated and the router redirects home; on
  /// failure the [ApiException] propagates to the form.
  Future<void> register({
    required String email,
    required String password,
    required String firstName,
    required String lastName,
    int? cityId,
  }) async {
    final user = await ref.read(authRepositoryProvider).register(
          email: email,
          password: password,
          firstName: firstName,
          lastName: lastName,
          cityId: cityId,
        );
    state = AuthState.authenticated(user);
  }

  /// Requests a password-reset link by email. Does not change auth state — the
  /// reset itself is completed on the web page the emailed link opens.
  Future<void> forgotPassword(String email) =>
      ref.read(authRepositoryProvider).forgotPassword(email);

  /// Revokes the session server-side and clears it locally.
  Future<void> logout() async {
    await ref.read(authRepositoryProvider).logout();
    state = const AuthState.unauthenticated();
  }
}

/// The single source of truth for who is signed in.
final authControllerProvider =
    NotifierProvider<AuthController, AuthState>(AuthController.new);
