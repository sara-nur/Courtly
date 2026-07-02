import 'package:dio/dio.dart';

import '../../reference_data/domain/reference_models.dart' show PagedResult;
import '../domain/user_detail.dart';
import '../domain/user_summary.dart';

/// Thin transport over the Feature 15/15A admin user endpoints. Shapes requests
/// (path, query, JSON bodies) and parses responses; failures propagate as
/// [DioException] and the repository normalizes them to a typed `ApiException`.
///
///   GET /api/users              (paged + optional `search` name/email term) [Admin/Staff]
///   GET /api/users/{id}         (detail = profile + roles + active state)   [Admin/Staff]
///   PUT /api/users/{id}/active  ({isActive} → activate / deactivate)        [Admin]
///   PUT /api/users/{id}/role    ({role} → assign the user's role)           [Admin]
class UserApi {
  UserApi(this._dio);

  final Dio _dio;

  static const String _base = '/api/users';

  Future<PagedResult<UserSummary>> search({
    int page = 1,
    int pageSize = 20,
    String? search,
  }) async {
    final query = <String, dynamic>{'page': page, 'pageSize': pageSize};
    if (search != null && search.trim().isNotEmpty) {
      query['search'] = search.trim();
    }
    final response = await _dio.get<dynamic>(_base, queryParameters: query);
    return PagedResult<UserSummary>.fromJson(
      (response.data as Map).cast<String, dynamic>(),
      UserSummary.fromJson,
    );
  }

  Future<UserDetail> getById(String id) async {
    final response = await _dio.get<dynamic>('$_base/$id');
    return UserDetail.fromJson((response.data as Map).cast<String, dynamic>());
  }

  Future<UserDetail> setActive(String id, bool isActive) async {
    final response = await _dio.put<dynamic>(
      '$_base/$id/active',
      data: <String, dynamic>{'isActive': isActive},
    );
    return UserDetail.fromJson((response.data as Map).cast<String, dynamic>());
  }

  Future<UserDetail> assignRole(String id, String role) async {
    final response = await _dio.put<dynamic>(
      '$_base/$id/role',
      data: <String, dynamic>{'role': role},
    );
    return UserDetail.fromJson((response.data as Map).cast<String, dynamic>());
  }
}
