import 'package:dio/dio.dart';

import '../../../core/network/api_exception.dart';
import '../domain/dashboard_models.dart';
import 'dashboard_api.dart';

/// Repository over [DashboardApi] — normalizes transport failures to a typed
/// [ApiException] so the UI surfaces the backend message, not a raw Dio error.
class DashboardRepository {
  DashboardRepository(this._api);

  final DashboardApi _api;

  Future<DashboardMetrics> getMetrics({
    DateTime? fromUtc,
    DateTime? toUtc,
    int? courtTypeId,
  }) async {
    try {
      return await _api.getMetrics(
        fromUtc: fromUtc,
        toUtc: toUtc,
        courtTypeId: courtTypeId,
      );
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }
}
