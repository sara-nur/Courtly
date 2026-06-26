import 'package:dio/dio.dart';

import '../../reference_data/domain/reference_models.dart' show PagedResult;
import '../domain/user_summary.dart';

/// Thin transport over the Feature 15 admin user endpoint. Shapes the request
/// (path, query) and parses the paged response; failures propagate as
/// [DioException] and the repository normalizes them to a typed `ApiException`.
///
///   GET /api/users   (paged + optional `search` name/email term)   [Admin/Staff]
class UserApi {
  UserApi(this._dio);

  final Dio _dio;

  static const String _users = '/api/users';

  Future<PagedResult<UserSummary>> search({
    int page = 1,
    int pageSize = 20,
    String? search,
  }) async {
    final query = <String, dynamic>{'page': page, 'pageSize': pageSize};
    if (search != null && search.trim().isNotEmpty) {
      query['search'] = search.trim();
    }
    final response = await _dio.get<dynamic>(_users, queryParameters: query);
    return PagedResult<UserSummary>.fromJson(
      (response.data as Map).cast<String, dynamic>(),
      UserSummary.fromJson,
    );
  }
}
