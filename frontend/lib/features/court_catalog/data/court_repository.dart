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
    double? minRating,
    bool? underMaintenance,
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
        minRating: minRating,
        underMaintenance: underMaintenance,
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

  // --- Court maintenance (F12) -----------------------------------------------

  Future<PagedResult<CourtMaintenanceLog>> listMaintenance(
    int courtId, {
    int page = 1,
    int pageSize = 50,
  }) async {
    try {
      return await _api.listMaintenance(courtId, page: page, pageSize: pageSize);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<CourtMaintenanceLog> createMaintenance(
    int courtId, {
    required String reason,
    DateTime? startUtc,
    DateTime? endUtc,
  }) async {
    try {
      return await _api.createMaintenance(
        courtId,
        reason: reason,
        startUtc: startUtc,
        endUtc: endUtc,
      );
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<CourtMaintenanceLog> startMaintenance(int courtId, int logId) async {
    try {
      return await _api.startMaintenance(courtId, logId);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<CourtMaintenanceLog> fixMaintenance(int courtId, int logId) async {
    try {
      return await _api.fixMaintenance(courtId, logId);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<CourtMaintenanceLog> cancelMaintenance(int courtId, int logId) async {
    try {
      return await _api.cancelMaintenance(courtId, logId);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  // --- Court time slots (F13) ------------------------------------------------

  Future<GenerateSlotsResult> generateSlots(
    int courtId, {
    required DateTime fromDate,
    required DateTime toDate,
    required int openHour,
    required int closeHour,
    required int slotMinutes,
    double? eveningPeakMultiplier,
  }) async {
    try {
      return await _api.generateSlots(
        courtId,
        fromDate: fromDate,
        toDate: toDate,
        openHour: openHour,
        closeHour: closeHour,
        slotMinutes: slotMinutes,
        eveningPeakMultiplier: eveningPeakMultiplier,
      );
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<DayAvailability> availability(int courtId, DateTime date) async {
    try {
      return await _api.availability(courtId, date);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<void> removeSlot(int courtId, int slotId) async {
    try {
      await _api.removeSlot(courtId, slotId);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<RemoveSlotsResult> removeDaySlots(int courtId, DateTime date) async {
    try {
      return await _api.removeDaySlots(courtId, date);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }
}
