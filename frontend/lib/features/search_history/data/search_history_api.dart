import 'package:dio/dio.dart';

/// Thin transport over the search-history endpoint. It only shapes the request
/// (path + JSON body); failures propagate as [DioException] and the repository
/// normalizes them to a typed `ApiException`.
///
/// Endpoint (must match the backend exactly):
///   POST /api/search-history   ({surfaceTypeId, courtTypeId, minPrice,
///                                 maxPrice, indoorOnly, rawQuery})
///
/// Records what the customer searched for so the app can surface recent/popular
/// searches later. Every field is optional — only the criteria the UI actually
/// set travel in the body.
class SearchHistoryApi {
  SearchHistoryApi(this._dio);

  final Dio _dio;

  static const String _base = '/api/search-history';

  Future<void> record({
    int? surfaceTypeId,
    int? courtTypeId,
    double? minPrice,
    double? maxPrice,
    bool? indoorOnly,
    String? rawQuery,
  }) async {
    await _dio.post<dynamic>(
      _base,
      data: <String, dynamic>{
        'surfaceTypeId': surfaceTypeId,
        'courtTypeId': courtTypeId,
        'minPrice': minPrice,
        'maxPrice': maxPrice,
        'indoorOnly': indoorOnly,
        'rawQuery': rawQuery,
      },
    );
  }
}
