import 'dart:typed_data';

import 'package:dio/dio.dart';

import '../../auth/domain/auth_models.dart';

/// Thin transport over the feature-28 self-service profile endpoints on
/// `/api/auth`. It only shapes requests and parses responses; failures propagate
/// as [DioException] for [ProfileRepository] to normalize. All calls are
/// authenticated — the Dio interceptor attaches the JWT and the server derives
/// identity from it (no user id is ever sent).
class ProfileApi {
  ProfileApi(this._dio);

  final Dio _dio;

  static const String _base = '/api/auth';

  /// `PUT /me` — updates personal data (name, email, city). Returns the updated
  /// user (same shape as `GET /me`), so the caller can refresh its cached user.
  Future<AuthUser> updateProfile({
    required String firstName,
    required String lastName,
    required String email,
    int? cityId,
  }) async {
    final response = await _dio.put<dynamic>(
      '$_base/me',
      data: {
        'firstName': firstName,
        'lastName': lastName,
        'email': email,
        'cityId': cityId,
      },
    );
    return AuthUser.fromJson((response.data as Map).cast<String, dynamic>());
  }

  /// `POST /change-password` — the server confirms the current password before
  /// setting the new one (rubric §294). Returns nothing (204 on success).
  Future<void> changePassword({
    required String currentPassword,
    required String newPassword,
  }) async {
    await _dio.post<dynamic>(
      '$_base/change-password',
      data: {
        'currentPassword': currentPassword,
        'newPassword': newPassword,
      },
    );
  }

  /// `PUT /me/avatar` (multipart) — uploads the profile image bytes. Returns the
  /// updated user (its `avatarUrl` is now populated).
  Future<AuthUser> uploadAvatar({
    required List<int> bytes,
    required String filename,
  }) async {
    final form = FormData.fromMap({
      'file': MultipartFile.fromBytes(bytes, filename: filename),
    });
    final response = await _dio.put<dynamic>('$_base/me/avatar', data: form);
    return AuthUser.fromJson((response.data as Map).cast<String, dynamic>());
  }

  /// `GET /me/avatar` — streams the stored avatar bytes (authenticated). Throws a
  /// [DioException] with a 404 response when the user has no avatar; the
  /// repository maps that to `null`.
  Future<Uint8List> fetchAvatarBytes() async {
    final response = await _dio.get<List<int>>(
      '$_base/me/avatar',
      options: Options(responseType: ResponseType.bytes),
    );
    return Uint8List.fromList(response.data ?? const <int>[]);
  }
}
