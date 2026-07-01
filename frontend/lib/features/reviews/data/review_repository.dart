import 'package:dio/dio.dart';

import '../../../core/network/api_exception.dart';
import '../../reference_data/domain/reference_models.dart' show PagedResult;
import '../domain/review_models.dart';
import 'review_api.dart';

/// Wraps [ReviewApi] and normalizes every failure to a typed [ApiException], so
/// the UI/controllers only ever see one error type — including the backend's
/// guard messages ("You can only review a completed booking", "You have already
/// reviewed this booking"). Mirrors [court_repository].
class ReviewRepository {
  ReviewRepository(this._api);

  final ReviewApi _api;

  Future<PagedResult<Review>> listForCourt(
    int courtId, {
    int page = 1,
    int pageSize = 10,
  }) async {
    try {
      return await _api.listForCourt(courtId, page: page, pageSize: pageSize);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<ReviewEligibility> eligibility(int courtId) async {
    try {
      return await _api.eligibility(courtId);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<Review> create({
    required int reservationId,
    required int rating,
    String? comment,
  }) async {
    try {
      return await _api.create(
        reservationId: reservationId,
        rating: rating,
        comment: comment,
      );
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }
}
