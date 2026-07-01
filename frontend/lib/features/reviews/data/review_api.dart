import 'package:dio/dio.dart';

import '../../reference_data/domain/reference_models.dart' show PagedResult;
import '../domain/review_models.dart';

/// Thin transport over the Feature 24 review endpoints. It only shapes requests
/// and parses responses; failures propagate as [DioException] and the repository
/// normalizes them to a typed `ApiException`.
///
/// Endpoints (must match the backend exactly):
///   GET  /api/courts/{courtId}/reviews             (paged, newest-first)
///   GET  /api/courts/{courtId}/review-eligibility  → ReviewEligibilityDto
///   POST /api/reviews                              ({reservationId, rating, comment})
class ReviewApi {
  ReviewApi(this._dio);

  final Dio _dio;

  Future<PagedResult<Review>> listForCourt(
    int courtId, {
    int page = 1,
    int pageSize = 10,
  }) async {
    final response = await _dio.get<dynamic>(
      '/api/courts/$courtId/reviews',
      queryParameters: {'page': page, 'pageSize': pageSize},
    );
    return PagedResult<Review>.fromJson(
      (response.data as Map).cast<String, dynamic>(),
      Review.fromJson,
    );
  }

  Future<ReviewEligibility> eligibility(int courtId) async {
    final response =
        await _dio.get<dynamic>('/api/courts/$courtId/review-eligibility');
    return ReviewEligibility.fromJson(
        (response.data as Map).cast<String, dynamic>());
  }

  Future<Review> create({
    required int reservationId,
    required int rating,
    String? comment,
  }) async {
    final response = await _dio.post<dynamic>(
      '/api/reviews',
      data: <String, dynamic>{
        'reservationId': reservationId,
        'rating': rating,
        'comment': comment,
      },
    );
    return Review.fromJson((response.data as Map).cast<String, dynamic>());
  }
}
