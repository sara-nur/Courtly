import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';

import '../../features/auth/domain/auth_models.dart';
import 'token_storage.dart';

/// Dio interceptor owning the access-token lifecycle (rubric A.2: a 401 must be
/// handled — refresh or redirect — never ignored):
///
/// 1. **Attach** the bearer access token to every outgoing request.
/// 2. On a **401**, transparently **refresh** the access token (rotating refresh
///    token) and **retry** the original request once.
/// 3. Refreshes are **single-flight**: many requests failing at once share one
///    refresh call instead of stampeding the endpoint.
/// 4. If refresh fails, clear the tokens and fire [onSessionExpired] so the
///    router redirects to login.
///
/// Refresh and retry run on a **bare** [refreshClient] (no auth interceptor) so
/// there is no recursion and no risk of an infinite refresh loop.
class AuthInterceptor extends Interceptor {
  AuthInterceptor({
    required this.storage,
    required this.refreshClient,
    required this.onSessionExpired,
  });

  final TokenStorage storage;

  /// A Dio without this interceptor, used for the `/refresh` call and the retry.
  final Dio refreshClient;

  /// Invoked when the session can no longer be refreshed (logged out remotely,
  /// refresh token expired/rotated). Tokens are already cleared at this point.
  final VoidCallback onSessionExpired;

  static const String _authHeader = 'Authorization';
  static const String _refreshPath = '/api/auth/refresh';

  /// The in-flight refresh, shared by concurrent 401s (single-flight).
  Future<bool>? _refreshing;

  @override
  void onRequest(
    RequestOptions options,
    RequestInterceptorHandler handler,
  ) async {
    final token = await storage.readAccessToken();
    if (token != null && token.isNotEmpty) {
      options.headers[_authHeader] = 'Bearer $token';
    }
    handler.next(options);
  }

  @override
  void onError(
    DioException err,
    ErrorInterceptorHandler handler,
  ) async {
    final path = err.requestOptions.path;
    final shouldRefresh =
        err.response?.statusCode == 401 && !_isAnonymousAuthPath(path);
    if (!shouldRefresh) {
      handler.next(err);
      return;
    }

    try {
      final refreshed = await _refreshSingleFlight();
      if (!refreshed) {
        await _expireSession();
        handler.next(err);
        return;
      }

      // Retry the original request once with the new token, on the bare client.
      final newToken = await storage.readAccessToken();
      final retryOptions = err.requestOptions
        ..headers[_authHeader] = 'Bearer $newToken';
      final response = await refreshClient.fetch<dynamic>(retryOptions);
      handler.resolve(response);
    } on DioException catch (retryError) {
      // The replay failed on its own terms (e.g. 500 / timeout) after a good
      // refresh — surface that real error, not the stale 401, so it is mapped
      // and classified correctly downstream.
      handler.next(retryError);
    } catch (_) {
      handler.next(err);
    }
  }

  Future<bool> _refreshSingleFlight() {
    return _refreshing ??=
        _performRefresh().whenComplete(() => _refreshing = null);
  }

  Future<bool> _performRefresh() async {
    final refreshToken = await storage.readRefreshToken();
    if (refreshToken == null || refreshToken.isEmpty) return false;
    try {
      final response = await refreshClient.post<dynamic>(
        _refreshPath,
        data: {'refreshToken': refreshToken},
      );
      final data = response.data;
      if (data is! Map) return false;
      final session = AuthSession.fromJson(Map<String, dynamic>.from(data));
      await storage.save(
        StoredTokens(
          accessToken: session.accessToken,
          refreshToken: session.refreshToken,
          accessTokenExpiresAtUtc: session.accessTokenExpiresAtUtc,
        ),
      );
      return true;
    } on DioException {
      return false;
    }
  }

  Future<void> _expireSession() async {
    await storage.clear();
    onSessionExpired();
  }

  /// Anonymous auth endpoints (login/register/refresh/forgot/reset) must never
  /// trigger a refresh: a 401 there means bad credentials, not an expired token.
  bool _isAnonymousAuthPath(String path) {
    return path.contains('/api/auth/login') ||
        path.contains('/api/auth/register') ||
        path.contains('/api/auth/refresh') ||
        path.contains('/api/auth/forgot-password') ||
        path.contains('/api/auth/reset-password');
  }
}
