import 'package:dio/dio.dart';

import '../../../core/enums/reservation_status.dart';
import '../../reference_data/domain/reference_models.dart' show PagedResult;
import '../domain/reservation_models.dart';

/// Thin transport over the Feature 14/15 reservation endpoints. Shapes requests
/// (path, query, JSON bodies) and parses responses; failures propagate as
/// [DioException] and the repository normalizes them to a typed `ApiException`.
///
/// Endpoints (must match the backend exactly):
///   GET  /api/reservations              (admin list — paged + filters)     [Admin/Staff]
///   GET  /api/reservations/mine         (the signed-in customer's own — paged)
///   GET  /api/reservations/{id}         (detail = reservation+audits+payment)
///   POST /api/reservations              ({timeSlotId} → the signed-in customer books; owner from JWT)
///   POST /api/reservations/admin        ({timeSlotId, userId} → book for a customer) [Admin/Staff]
///   POST /api/reservations/{id}/confirm                                    [Admin/Staff]
///   POST /api/reservations/{id}/cancel  ({reason})
///   POST /api/reservations/{id}/complete                                   [Admin/Staff]
///   POST /api/reservations/{id}/reschedule ({newTimeSlotId})               [Admin/Staff]
class ReservationApi {
  ReservationApi(this._dio);

  final Dio _dio;

  static const String _base = '/api/reservations';

  Future<PagedResult<Reservation>> list({
    int page = 1,
    int pageSize = 20,
    ReservationStatus? status,
    int? courtId,
    String? userId,
    DateTime? fromUtc,
    DateTime? toUtc,
  }) async {
    final query = <String, dynamic>{'page': page, 'pageSize': pageSize};
    if (status != null) query['status'] = status.wireValue;
    if (courtId != null) query['courtId'] = courtId;
    if (userId != null && userId.isNotEmpty) query['userId'] = userId;
    if (fromUtc != null) query['fromUtc'] = fromUtc.toUtc().toIso8601String();
    if (toUtc != null) query['toUtc'] = toUtc.toUtc().toIso8601String();

    final response = await _dio.get<dynamic>(_base, queryParameters: query);
    return PagedResult<Reservation>.fromJson(
      (response.data as Map).cast<String, dynamic>(),
      Reservation.fromJson,
    );
  }

  /// The signed-in customer's own reservations (GET `/api/reservations/mine`,
  /// newest-first), returned in the standard [PagedResult] envelope. [status] is
  /// the backend integer enum value (omit for all statuses).
  Future<PagedResult<Reservation>> listMine({
    int page = 1,
    int pageSize = 20,
    int? status,
    DateTime? fromUtc,
    DateTime? toUtc,
  }) async {
    final query = <String, dynamic>{'page': page, 'pageSize': pageSize};
    if (status != null) query['status'] = status;
    if (fromUtc != null) query['fromUtc'] = fromUtc.toUtc().toIso8601String();
    if (toUtc != null) query['toUtc'] = toUtc.toUtc().toIso8601String();

    final response = await _dio.get<dynamic>('$_base/mine', queryParameters: query);
    return PagedResult<Reservation>.fromJson(
      (response.data as Map).cast<String, dynamic>(),
      Reservation.fromJson,
    );
  }

  Future<ReservationDetail> getById(int id) async {
    final response = await _dio.get<dynamic>('$_base/$id');
    return ReservationDetail.fromJson((response.data as Map).cast<String, dynamic>());
  }

  /// The signed-in customer books a slot for themselves (POST `/api/reservations`).
  /// The body carries only the slot id — the backend derives the owner from the
  /// JWT and the price from the slot catalog (never trusted from the client).
  Future<ReservationDetail> create({required int timeSlotId}) async {
    final response = await _dio.post<dynamic>(
      _base,
      data: <String, dynamic>{'timeSlotId': timeSlotId},
    );
    return ReservationDetail.fromJson((response.data as Map).cast<String, dynamic>());
  }

  Future<ReservationDetail> createForUser({
    required int timeSlotId,
    required String userId,
  }) async {
    final response = await _dio.post<dynamic>(
      '$_base/admin',
      data: <String, dynamic>{'timeSlotId': timeSlotId, 'userId': userId},
    );
    return ReservationDetail.fromJson((response.data as Map).cast<String, dynamic>());
  }

  Future<ReservationDetail> confirm(int id) => _transition(id, 'confirm');

  Future<ReservationDetail> complete(int id) => _transition(id, 'complete');

  Future<ReservationDetail> cancel(int id, String reason) async {
    final response = await _dio.post<dynamic>(
      '$_base/$id/cancel',
      data: <String, dynamic>{'reason': reason},
    );
    return ReservationDetail.fromJson((response.data as Map).cast<String, dynamic>());
  }

  Future<ReservationDetail> reschedule(int id, int newTimeSlotId) async {
    final response = await _dio.post<dynamic>(
      '$_base/$id/reschedule',
      data: <String, dynamic>{'newTimeSlotId': newTimeSlotId},
    );
    return ReservationDetail.fromJson((response.data as Map).cast<String, dynamic>());
  }

  Future<ReservationDetail> _transition(int id, String action) async {
    final response = await _dio.post<dynamic>('$_base/$id/$action');
    return ReservationDetail.fromJson((response.data as Map).cast<String, dynamic>());
  }
}
