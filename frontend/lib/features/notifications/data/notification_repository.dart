// F27 — Client notifications UI. Wraps [NotificationApi] and normalizes failures.
//
// Every call funnels through a `try/on DioException` so the UI/controllers only
// ever see one error type ([ApiException]). Mirrors [review_repository].

import 'package:dio/dio.dart';

import '../../../core/network/api_exception.dart';
import '../../reference_data/domain/reference_models.dart' show PagedResult;
import '../domain/notification_models.dart';
import 'notification_api.dart';

class NotificationRepository {
  NotificationRepository(this._api);

  final NotificationApi _api;

  Future<PagedResult<AppNotification>> list({
    int page = 1,
    int pageSize = 20,
  }) async {
    try {
      return await _api.list(page: page, pageSize: pageSize);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<int> unreadCount() async {
    try {
      return await _api.unreadCount();
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<AppNotification> markRead(int id) async {
    try {
      return await _api.markRead(id);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<void> markAllRead() async {
    try {
      await _api.markAllRead();
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }
}
