import 'dart:typed_data';

import 'package:dio/dio.dart';

import '../../../core/enums/reservation_status.dart';
import '../domain/report_models.dart';

/// Thin transport over the feature-20 report endpoints. The `/data` endpoints return the report rows as JSON (for the
/// in-app table); the bare endpoints return the rendered PDF as raw bytes (for the preview/print path). Each call
/// shapes the query; failures propagate as [DioException] and the repository normalizes them.
///
/// Endpoints (must match the backend exactly):
///   GET /api/reports/reservations[/data]?fromUtc&toUtc&courtId&status        [Admin/Staff]
///   GET /api/reports/revenue-utilization[/data]?year&month&courtId           [Admin/Staff]
class ReportsApi {
  ReportsApi(this._dio);

  final Dio _dio;

  Map<String, dynamic> _reservationsQuery({
    DateTime? fromUtc,
    DateTime? toUtc,
    int? courtId,
    ReservationStatus? status,
  }) {
    final query = <String, dynamic>{};
    if (fromUtc != null) query['fromUtc'] = fromUtc.toUtc().toIso8601String();
    if (toUtc != null) query['toUtc'] = toUtc.toUtc().toIso8601String();
    if (courtId != null) query['courtId'] = courtId;
    if (status != null) query['status'] = status.wireValue;
    return query;
  }

  Map<String, dynamic> _revenueQuery({required int year, required int month, int? courtId}) {
    final query = <String, dynamic>{'year': year, 'month': month};
    if (courtId != null) query['courtId'] = courtId;
    return query;
  }

  Future<ReservationsReportData> reservationsReportData({
    DateTime? fromUtc,
    DateTime? toUtc,
    int? courtId,
    ReservationStatus? status,
  }) async {
    final response = await _dio.get<dynamic>(
      '/api/reports/reservations/data',
      queryParameters: _reservationsQuery(fromUtc: fromUtc, toUtc: toUtc, courtId: courtId, status: status),
    );
    return ReservationsReportData.fromJson((response.data as Map).cast<String, dynamic>());
  }

  Future<RevenueUtilizationReportData> revenueUtilizationReportData({
    required int year,
    required int month,
    int? courtId,
  }) async {
    final response = await _dio.get<dynamic>(
      '/api/reports/revenue-utilization/data',
      queryParameters: _revenueQuery(year: year, month: month, courtId: courtId),
    );
    return RevenueUtilizationReportData.fromJson((response.data as Map).cast<String, dynamic>());
  }

  Future<Uint8List> reservationsReport({
    DateTime? fromUtc,
    DateTime? toUtc,
    int? courtId,
    ReservationStatus? status,
  }) async {
    final response = await _dio.get<List<int>>(
      '/api/reports/reservations',
      queryParameters: _reservationsQuery(fromUtc: fromUtc, toUtc: toUtc, courtId: courtId, status: status),
      options: Options(responseType: ResponseType.bytes),
    );
    return Uint8List.fromList(response.data ?? const <int>[]);
  }

  Future<Uint8List> revenueUtilizationReport({
    required int year,
    required int month,
    int? courtId,
  }) async {
    final response = await _dio.get<List<int>>(
      '/api/reports/revenue-utilization',
      queryParameters: _revenueQuery(year: year, month: month, courtId: courtId),
      options: Options(responseType: ResponseType.bytes),
    );
    return Uint8List.fromList(response.data ?? const <int>[]);
  }
}
