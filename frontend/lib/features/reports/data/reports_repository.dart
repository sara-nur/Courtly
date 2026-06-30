import 'dart:typed_data';

import 'package:dio/dio.dart';

import '../../../core/enums/reservation_status.dart';
import '../../../core/network/api_exception.dart';
import '../domain/report_models.dart';
import 'reports_api.dart';

/// Repository over [ReportsApi] — normalizes transport failures to a typed [ApiException] so the UI surfaces the
/// backend message, not a raw Dio error. The `*Data` methods back the in-app table; the byte methods back the
/// preview/print path.
class ReportsRepository {
  ReportsRepository(this._api);

  final ReportsApi _api;

  Future<ReservationsReportData> reservationsReportData({
    DateTime? fromUtc,
    DateTime? toUtc,
    int? courtId,
    ReservationStatus? status,
  }) async {
    try {
      return await _api.reservationsReportData(
        fromUtc: fromUtc,
        toUtc: toUtc,
        courtId: courtId,
        status: status,
      );
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<RevenueUtilizationReportData> revenueUtilizationReportData({
    required int year,
    required int month,
    int? courtId,
  }) async {
    try {
      return await _api.revenueUtilizationReportData(year: year, month: month, courtId: courtId);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<Uint8List> reservationsReport({
    DateTime? fromUtc,
    DateTime? toUtc,
    int? courtId,
    ReservationStatus? status,
  }) async {
    try {
      return await _api.reservationsReport(
        fromUtc: fromUtc,
        toUtc: toUtc,
        courtId: courtId,
        status: status,
      );
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<Uint8List> revenueUtilizationReport({
    required int year,
    required int month,
    int? courtId,
  }) async {
    try {
      return await _api.revenueUtilizationReport(year: year, month: month, courtId: courtId);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }
}
