import 'package:dio/dio.dart';

import '../../../core/network/api_exception.dart';
import '../domain/recommendation_models.dart';
import 'recommendations_api.dart';

/// Wraps [RecommendationsApi] and normalizes every failure to a typed
/// [ApiException], so the UI/controllers only ever see one error type. The method
/// surface mirrors the API one-for-one. Mirrors [court_repository].
class RecommendationsRepository {
  RecommendationsRepository(this._api);

  final RecommendationsApi _api;

  Future<RecommendationBatch> list({int page = 1, int pageSize = 20}) async {
    try {
      return await _api.list(page: page, pageSize: pageSize);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<void> submitFeedback({
    required bool isHelpful,
    required List<int> courtIds,
  }) async {
    try {
      await _api.submitFeedback(isHelpful: isHelpful, courtIds: courtIds);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }
}
