import 'package:dio/dio.dart';

import '../../../core/network/api_exception.dart';
import '../domain/news_models.dart';
import 'news_api.dart';

/// Wraps [NewsApi] and normalizes every failure to a typed [ApiException]
/// (`try { … } on DioException catch (e) { throw ApiException.from(e); }`), so the
/// UI/controllers only ever see one error type — including the backend's per-field
/// validation messages (title/text/published date/image).
///
/// The method surface mirrors the API one-for-one, typed to [News].
class NewsRepository {
  NewsRepository(this._api);

  final NewsApi _api;

  Future<PagedResult<News>> list({
    int page = 1,
    int pageSize = 20,
    String? search,
    bool? activeOnly,
  }) async {
    try {
      return await _api.list(
        page: page,
        pageSize: pageSize,
        search: search,
        activeOnly: activeOnly,
      );
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<News> create({
    required String title,
    required String text,
    required DateTime publishedAtUtc,
    required bool isActive,
    required List<int> imageBytes,
    required String filename,
  }) async {
    try {
      return await _api.create(
        title: title,
        text: text,
        publishedAtUtc: publishedAtUtc,
        isActive: isActive,
        imageBytes: imageBytes,
        filename: filename,
      );
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<News> update(
    int id, {
    required String title,
    required String text,
    required DateTime publishedAtUtc,
    required bool isActive,
    List<int>? imageBytes,
    String? filename,
  }) async {
    try {
      return await _api.update(
        id,
        title: title,
        text: text,
        publishedAtUtc: publishedAtUtc,
        isActive: isActive,
        imageBytes: imageBytes,
        filename: filename,
      );
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
}
