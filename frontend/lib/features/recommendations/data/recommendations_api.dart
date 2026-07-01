import 'package:dio/dio.dart';

import '../domain/recommendation_models.dart';

/// Thin transport over the Feature 29 recommender endpoints. It only shapes the
/// request and parses the response; failures propagate as [DioException] and the
/// repository normalizes them to a typed `ApiException`.
///
/// Endpoints (must match the backend exactly):
///   GET  /api/recommendations                 (paged; owner from JWT)
///   POST /api/recommendations/feedback        ({isHelpful, courtIds})  → 204
class RecommendationsApi {
  RecommendationsApi(this._dio);

  final Dio _dio;

  static const String _base = '/api/recommendations';

  Future<RecommendationBatch> list({int page = 1, int pageSize = 20}) async {
    final response = await _dio.get<dynamic>(
      _base,
      queryParameters: {'page': page, 'pageSize': pageSize},
    );
    return RecommendationBatch.fromJson((response.data as Map).cast<String, dynamic>());
  }

  Future<void> submitFeedback({
    required bool isHelpful,
    required List<int> courtIds,
  }) async {
    await _dio.post<dynamic>(
      '$_base/feedback',
      data: <String, dynamic>{
        'isHelpful': isHelpful,
        'courtIds': courtIds,
      },
    );
  }
}
