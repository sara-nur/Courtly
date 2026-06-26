import 'package:dio/dio.dart';

import '../../../core/network/api_exception.dart';
import '../../reference_data/domain/reference_models.dart' show PagedResult;
import '../domain/user_summary.dart';
import 'user_api.dart';

/// Wraps [UserApi] and normalizes every failure to a typed [ApiException], so
/// the UI/controllers only ever see one error type.
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
}
