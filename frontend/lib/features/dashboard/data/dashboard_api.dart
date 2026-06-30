import 'package:dio/dio.dart';

import '../domain/dashboard_models.dart';

/// Thin transport over the Feature 19 dashboard endpoint. Shapes the query
/// (optional date range + court type) and parses the composite response;
/// failures propagate as [DioException] and the repository normalizes them.
///
/// Endpoint (must match the backend exactly):
///   GET /api/dashboard/metrics?fromUtc&toUtc&courtTypeId   [Admin/Staff]
class DashboardApi {
  DashboardApi(this._dio);

  final Dio _dio;

  static const String _base = '/api/dashboard/metrics';

  Future<DashboardMetrics> getMetrics({
    DateTime? fromUtc,
    DateTime? toUtc,
    int? courtTypeId,
  }) async {
    final query = <String, dynamic>{};
    if (fromUtc != null) query['fromUtc'] = fromUtc.toUtc().toIso8601String();
    if (toUtc != null) query['toUtc'] = toUtc.toUtc().toIso8601String();
    if (courtTypeId != null) query['courtTypeId'] = courtTypeId;

    final response = await _dio.get<dynamic>(_base, queryParameters: query);
    return DashboardMetrics.fromJson((response.data as Map).cast<String, dynamic>());
  }
}
