import 'package:dio/dio.dart';

import '../../../core/network/api_exception.dart';
import '../../../core/network/token_storage.dart';
import '../domain/auth_models.dart';
import 'auth_api.dart';

/// Orchestrates authentication: calls [AuthApi], persists tokens in
/// [TokenStorage], and enforces the app's **role gate** (rubric §5). Each app
/// admits only its [allowedRoles] (admin app: Admin + Staff; client app: User);
/// an account that authenticates successfully but lacks an allowed role is
/// rejected and its just-issued tokens are revoked. All failures surface as a
/// typed [ApiException].
class AuthRepository {
  AuthRepository({
    required AuthApi api,
    required TokenStorage storage,
    required Set<String> allowedRoles,
  })  : _api = api,
        _storage = storage,
        _allowedRoles = allowedRoles;

  final AuthApi _api;
  final TokenStorage _storage;
  final Set<String> _allowedRoles;

  /// Logs in, enforces the role gate, persists tokens, and returns the user.
  /// Throws [ApiException] on bad credentials, a blocked role, or a network
  /// failure.
  Future<AuthUser> login(String userNameOrEmail, String password) async {
    final AuthSession session;
    try {
      session = await _api.login(userNameOrEmail, password);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }

    // Persist first so the (authorized) logout call below can present the token.
    try {
      await _storage.save(
        StoredTokens(
          accessToken: session.accessToken,
          refreshToken: session.refreshToken,
          accessTokenExpiresAtUtc: session.accessTokenExpiresAtUtc,
        ),
      );
    } catch (_) {
      // Secure storage (OS keychain/keystore) is unavailable — surface a clear,
      // typed message instead of leaking a raw PlatformException to the UI as a
      // generic "Something went wrong".
      throw const ApiException(
        message: 'Could not securely store your session on this device. '
            'Please try again.',
      );
    }

    if (!session.user.hasAnyRole(_allowedRoles)) {
      // Valid credentials, wrong app: revoke server-side and clear locally.
      await logout();
      throw const ApiException(
        message: 'This account does not have access to this app.',
        statusCode: 403,
      );
    }

    return session.user;
  }

  /// Registers a new account and signs it in. The server auto-logs-in and always
  /// grants the `User` role, so the client's role gate passes. Mirrors [login]:
  /// persist the issued tokens, enforce the gate, return the user. Throws
  /// [ApiException] on validation failure (e.g. duplicate email, weak password).
  Future<AuthUser> register({
    required String email,
    required String password,
    required String firstName,
    required String lastName,
    int? cityId,
  }) async {
    final AuthSession session;
    try {
      session = await _api.register(
        email: email,
        password: password,
        firstName: firstName,
        lastName: lastName,
        cityId: cityId,
      );
    } on DioException catch (e) {
      throw ApiException.from(e);
    }

    try {
      await _storage.save(
        StoredTokens(
          accessToken: session.accessToken,
          refreshToken: session.refreshToken,
          accessTokenExpiresAtUtc: session.accessTokenExpiresAtUtc,
        ),
      );
    } catch (_) {
      throw const ApiException(
        message: 'Could not securely store your session on this device. '
            'Please try again.',
      );
    }

    if (!session.user.hasAnyRole(_allowedRoles)) {
      await logout();
      throw const ApiException(
        message: 'This account does not have access to this app.',
        statusCode: 403,
      );
    }

    return session.user;
  }

  /// Requests a password-reset link by email (anti-enumeration: always succeeds
  /// server-side). Throws [ApiException] only on a transport/validation failure.
  Future<void> forgotPassword(String email) async {
    try {
      await _api.forgotPassword(email);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  /// Restores a session on startup. Returns the user when a stored token still
  /// validates (the interceptor refreshes it if the access token expired) and
  /// the role gate passes; otherwise `null`. Only a genuine 401 clears the
  /// tokens — a transient network error keeps them for a later retry.
  Future<AuthUser?> restore() async {
    final tokens = await _storage.read();
    if (tokens == null) return null;
    try {
      final user = await _api.me();
      if (!user.hasAnyRole(_allowedRoles)) {
        await logout();
        return null;
      }
      return user;
    } on DioException catch (e) {
      if (e.response?.statusCode == 401) {
        await _storage.clear();
      }
      return null;
    }
  }

  /// Revokes the session server-side (best effort) and clears local tokens.
  Future<void> logout() async {
    final refreshToken = await _storage.readRefreshToken();
    if (refreshToken != null && refreshToken.isNotEmpty) {
      try {
        await _api.logout(refreshToken);
      } on DioException {
        // Best effort — clear locally even if the network call fails.
      }
    }
    await _storage.clear();
  }
}
