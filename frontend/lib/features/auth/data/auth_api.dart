import 'package:dio/dio.dart';

import '../domain/auth_models.dart';

/// Thin transport over the backend `/api/auth` endpoints (feature 5). It only
/// shapes requests and parses responses; failures propagate as [DioException]
/// (the error interceptor has already attached a typed `ApiException`), and the
/// repository normalizes them.
class AuthApi {
  AuthApi(this._dio);

  final Dio _dio;

  static const String _base = '/api/auth';

  /// `POST /login` — credentials in the body (never the query string, rubric §5).
  Future<AuthSession> login(String userNameOrEmail, String password) async {
    final response = await _dio.post<dynamic>(
      '$_base/login',
      data: {'userNameOrEmail': userNameOrEmail, 'password': password},
    );
    return AuthSession.fromJson((response.data as Map).cast<String, dynamic>());
  }

  /// `GET /me` — the authenticated user; also used to validate a restored token.
  Future<AuthUser> me() async {
    final response = await _dio.get<dynamic>('$_base/me');
    return AuthUser.fromJson((response.data as Map).cast<String, dynamic>());
  }

  /// `POST /logout` — server-side revocation of this access token's `jti` plus
  /// the presented refresh token (rubric §5: logout invalidates on the server,
  /// not just locally).
  Future<void> logout(String refreshToken) async {
    await _dio.post<dynamic>(
      '$_base/logout',
      data: {'refreshToken': refreshToken},
    );
  }
}
