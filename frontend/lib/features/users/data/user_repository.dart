import 'package:dio/dio.dart';

import '../../../core/network/api_exception.dart';
import '../../reference_data/domain/reference_models.dart' show PagedResult;
import '../domain/user_detail.dart';
import '../domain/user_summary.dart';
import 'user_api.dart';

/// Wraps [UserApi] and normalizes every failure to a typed [ApiException], so
/// the UI/controllers only ever see one error type (including backend business
/// messages like the self-deactivate guard). Mirrors the API surface one-for-one.
class UserRepository {
  UserRepository(this._api);

  final UserApi _api;

  Future<PagedResult<UserSummary>> search({
    int page = 1,
    int pageSize = 20,
    String? search,
  }) async {
    try {
      return await _api.search(page: page, pageSize: pageSize, search: search);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<UserDetail> getById(String id) async {
    try {
      return await _api.getById(id);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<UserDetail> setActive(String id, bool isActive) async {
    try {
      return await _api.setActive(id, isActive);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<UserDetail> assignRole(String id, String role) async {
    try {
      return await _api.assignRole(id, role);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }
}
