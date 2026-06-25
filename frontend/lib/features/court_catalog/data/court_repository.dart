import 'package:dio/dio.dart';

import '../../../core/network/api_exception.dart';
import '../domain/court_models.dart';
import 'court_api.dart';

/// Wraps [CourtApi] and normalizes every failure to a typed [ApiException]
/// (`try { … } on DioException catch (e) { throw ApiException.from(e); }`), so
/// the UI/controllers only ever see one error type — including the backend's
/// per-field validation messages and delete-restrict `BusinessException` text.
///
/// The method surface mirrors the API one-for-one, typed to [Court].
class CourtRepository {
  CourtRepository(this._api);

  final CourtApi _api;

  Future<PagedResult<Court>> list({
    int page = 1,
    int pageSize = 20,
    String? search,
    int? cityId,
    int? countryId,
    int? surfaceTypeId,
    int? courtTypeId,
    bool? isIndoor,
    bool? isActive,
    bool? isFeatured,
    double? minPrice,
    double? maxPrice,
  }) async {
    try {
      return await _api.list(
        page: page,
        pageSize: pageSize,
        search: search,
        cityId: cityId,
        countryId: countryId,
        surfaceTypeId: surfaceTypeId,
        courtTypeId: courtTypeId,
        isIndoor: isIndoor,
        isActive: isActive,
        isFeatured: isFeatured,
        minPrice: minPrice,
        maxPrice: maxPrice,
      );
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<Court> getById(int id) async {
    try {
      return await _api.getById(id);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<Court> create(Map<String, dynamic> payload) async {
    try {
      return await _api.create(payload);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<Court> update(int id, Map<String, dynamic> payload) async {
    try {
      return await _api.update(id, payload);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<void> delete(int id) async {
    try {
      await _api.delete(id);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  // --- Court images ----------------------------------------------------------

  Future<List<CourtImage>> listImages(int courtId) async {
    try {
      return await _api.listImages(courtId);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<CourtImage> uploadImage(
    int courtId, {
    required List<int> bytes,
    required String filename,
    String? caption,
    bool isPrimary = false,
  }) async {
    try {
      return await _api.uploadImage(
        courtId,
        bytes: bytes,
        filename: filename,
        caption: caption,
        isPrimary: isPrimary,
      );
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<CourtImage> setPrimaryImage(int courtId, int imageId) async {
    try {
      return await _api.setPrimaryImage(courtId, imageId);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<void> deleteImage(int courtId, int imageId) async {
    try {
      await _api.deleteImage(courtId, imageId);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  // --- Court amenities -------------------------------------------------------

  Future<List<CourtAmenityLink>> listAmenities(int courtId) async {
    try {
      return await _api.listAmenities(courtId);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<List<CourtAmenityLink>> setAmenities(
    int courtId,
    List<({int amenityId, String? note, bool isHighlighted})> items,
  ) async {
    try {
      return await _api.setAmenities(courtId, items);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }
}
