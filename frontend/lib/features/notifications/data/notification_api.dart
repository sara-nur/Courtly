// F27 — Client notifications UI. Thin transport over the notification endpoints.
//
// It only shapes requests and parses responses; failures propagate as
// [DioException] and the repository normalizes them to a typed `ApiException`.
// Ownership is derived from the JWT server-side, so no user id is ever sent.
//
// Endpoints (must match the backend exactly):
//   GET  /api/notifications?page=&pageSize=   (paged, newest-first, own only)
//   GET  /api/notifications/unread-count      → { "count": <int> }
//   POST /api/notifications/{id}/read         → NotificationDto (idempotent)
//   POST /api/notifications/read-all          → 204 No Content

import 'package:dio/dio.dart';

import '../../reference_data/domain/reference_models.dart' show PagedResult;
import '../domain/notification_models.dart';

class NotificationApi {
  NotificationApi(this._dio);

  final Dio _dio;

  Future<PagedResult<AppNotification>> list({
    int page = 1,
    int pageSize = 20,
  }) async {
    final response = await _dio.get<dynamic>(
      '/api/notifications',
      queryParameters: {'page': page, 'pageSize': pageSize},
    );
    return PagedResult<AppNotification>.fromJson(
      (response.data as Map).cast<String, dynamic>(),
      AppNotification.fromJson,
    );
  }

  Future<int> unreadCount() async {
    final response = await _dio.get<dynamic>('/api/notifications/unread-count');
    return ((response.data as Map)['count'] as num).toInt();
  }

  Future<AppNotification> markRead(int id) async {
    final response = await _dio.post<dynamic>('/api/notifications/$id/read');
    return AppNotification.fromJson(
        (response.data as Map).cast<String, dynamic>());
  }

  Future<void> markAllRead() async {
    await _dio.post<dynamic>('/api/notifications/read-all');
  }
}
