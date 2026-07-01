import 'dart:typed_data';

import 'package:dio/dio.dart';

import '../../../core/network/api_exception.dart';
import '../../auth/domain/auth_models.dart';
import 'profile_api.dart';

/// Orchestrates the self-service profile operations (feature 28): edit profile,
/// change password, upload/read the avatar. It wraps [ProfileApi] and normalizes
/// every failure to a typed [ApiException] so the screen can render backend
/// validation messages **below** the offending field (rubric §4).
class ProfileRepository {
  ProfileRepository(this._api);

  final ProfileApi _api;

  /// Updates name/email/city and returns the refreshed user.
  Future<AuthUser> updateProfile({
    required String firstName,
    required String lastName,
    required String email,
    int? cityId,
  }) async {
    try {
      return await _api.updateProfile(
        firstName: firstName,
        lastName: lastName,
        email: email,
        cityId: cityId,
      );
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  /// Changes the password after the server confirms the current one.
  Future<void> changePassword({
    required String currentPassword,
    required String newPassword,
  }) async {
    try {
      await _api.changePassword(
        currentPassword: currentPassword,
        newPassword: newPassword,
      );
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  /// Uploads the profile image and returns the refreshed user.
  Future<AuthUser> uploadAvatar({
    required List<int> bytes,
    required String filename,
  }) async {
    try {
      return await _api.uploadAvatar(bytes: bytes, filename: filename);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  /// Reads the stored avatar bytes, or `null` when the user has none (a 404 is
  /// an expected "no avatar" state, not an error).
  Future<Uint8List?> fetchAvatarBytes() async {
    try {
      return await _api.fetchAvatarBytes();
    } on DioException catch (e) {
      if (e.response?.statusCode == 404) return null;
      throw ApiException.from(e);
    }
  }
}
