import 'package:dio/dio.dart';

import '../../../core/enums/reservation_status.dart';
import '../../../core/network/api_exception.dart';
import '../../reference_data/domain/reference_models.dart' show PagedResult;
import '../domain/reservation_models.dart';
import 'reservation_api.dart';

/// Wraps [ReservationApi] and normalizes every failure to a typed [ApiException]
/// (so the UI sees one error type — including backend business messages like the
/// paid-cancel block and the 409 "slot was just taken"). Mirrors the API surface
/// one-for-one, typed to the reservation models.
class ReservationRepository {
  ReservationRepository(this._api);

  final ReservationApi _api;

  Future<PagedResult<Reservation>> list({
    int page = 1,
    int pageSize = 20,
    ReservationStatus? status,
    int? courtId,
    String? userId,
    DateTime? fromUtc,
    DateTime? toUtc,
  }) async {
    try {
      return await _api.list(
        page: page,
        pageSize: pageSize,
        status: status,
        courtId: courtId,
        userId: userId,
        fromUtc: fromUtc,
        toUtc: toUtc,
      );
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<PagedResult<Reservation>> listMine({
    int page = 1,
    int pageSize = 20,
    int? status,
    DateTime? fromUtc,
    DateTime? toUtc,
  }) async {
    try {
      return await _api.listMine(
        page: page,
        pageSize: pageSize,
        status: status,
        fromUtc: fromUtc,
        toUtc: toUtc,
      );
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<ReservationDetail> getById(int id) async {
    try {
      return await _api.getById(id);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  /// The signed-in customer books a slot for themselves (owner from the JWT).
  Future<ReservationDetail> create({required int timeSlotId}) async {
    try {
      return await _api.create(timeSlotId: timeSlotId);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<ReservationDetail> createForUser({
    required int timeSlotId,
    required String userId,
  }) async {
    try {
      return await _api.createForUser(timeSlotId: timeSlotId, userId: userId);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<ReservationDetail> confirm(int id) async {
    try {
      return await _api.confirm(id);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<ReservationDetail> complete(int id) async {
    try {
      return await _api.complete(id);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<ReservationDetail> cancel(int id, String reason) async {
    try {
      return await _api.cancel(id, reason);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<ReservationDetail> reschedule(int id, int newTimeSlotId) async {
    try {
      return await _api.reschedule(id, newTimeSlotId);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }
}
