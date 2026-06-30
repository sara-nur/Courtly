import 'package:dio/dio.dart';

import '../../../core/network/api_exception.dart';
import 'search_history_api.dart';

/// Wraps [SearchHistoryApi] and normalizes every failure to a typed
/// [ApiException] (`try { … } on DioException catch (e) { throw ApiException.from(e); }`),
/// so callers only ever see one error type.
///
/// The method surface mirrors the API one-for-one.
class SearchHistoryRepository {
  SearchHistoryRepository(this._api);

  final SearchHistoryApi _api;

  Future<void> record({
    int? surfaceTypeId,
    int? courtTypeId,
    double? minPrice,
    double? maxPrice,
    bool? indoorOnly,
    String? rawQuery,
  }) async {
    try {
      await _api.record(
        surfaceTypeId: surfaceTypeId,
        courtTypeId: courtTypeId,
        minPrice: minPrice,
        maxPrice: maxPrice,
        indoorOnly: indoorOnly,
        rawQuery: rawQuery,
      );
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }
}
