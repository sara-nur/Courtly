import 'package:dio/dio.dart';

import '../domain/news_models.dart';

/// Thin transport over the Feature 21 news endpoints. It only shapes requests
/// (path, query params, multipart bodies) and parses responses into [News];
/// failures propagate as [DioException] and the repository normalizes them to a
/// typed `ApiException`.
///
/// Create/update send `multipart/form-data` (text fields + the image file in one
/// request), mirroring the court image upload. The image is required on create
/// and optional on update (omit the file to keep the existing image).
///
/// Endpoints (must match the backend exactly):
///   GET        /api/news               (paged + search + activeOnly)
///   POST       /api/news               (multipart: title, text, publishedAtUtc, isActive, file)
///   PUT        /api/news/{id}           (multipart: same; file optional)
///   DELETE     /api/news/{id}
class NewsApi {
  NewsApi(this._dio);

  final Dio _dio;

  static const String _news = '/api/news';

  /// Builds the `{ page, pageSize, search, activeOnly }` query, omitting a blank
  /// search and an unset active filter so the backend only applies what was set.
  Map<String, dynamic> _listQuery(int page, int pageSize, String? search, bool? activeOnly) {
    final query = <String, dynamic>{'page': page, 'pageSize': pageSize};
    if (search != null && search.trim().isNotEmpty) {
      query['search'] = search.trim();
    }
    if (activeOnly != null) query['activeOnly'] = activeOnly;
    return query;
  }

  Future<PagedResult<News>> list({
    int page = 1,
    int pageSize = 20,
    String? search,
    bool? activeOnly,
  }) async {
    final response = await _dio.get<dynamic>(
      _news,
      queryParameters: _listQuery(page, pageSize, search, activeOnly),
    );
    return PagedResult<News>.fromJson(
      (response.data as Map).cast<String, dynamic>(),
      News.fromJson,
    );
  }

  Future<News> create({
    required String title,
    required String text,
    required DateTime publishedAtUtc,
    required bool isActive,
    required List<int> imageBytes,
    required String filename,
  }) async {
    final form = FormData.fromMap({
      'title': title,
      'text': text,
      'publishedAtUtc': publishedAtUtc.toUtc().toIso8601String(),
      'isActive': isActive,
      'file': MultipartFile.fromBytes(imageBytes, filename: filename),
    });
    final response = await _dio.post<dynamic>(_news, data: form);
    return News.fromJson((response.data as Map).cast<String, dynamic>());
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
    final form = FormData.fromMap({
      'title': title,
      'text': text,
      'publishedAtUtc': publishedAtUtc.toUtc().toIso8601String(),
      'isActive': isActive,
      // Only attach a file when the user picked a new one — otherwise the
      // backend keeps the existing image.
      if (imageBytes != null && filename != null)
        'file': MultipartFile.fromBytes(imageBytes, filename: filename),
    });
    final response = await _dio.put<dynamic>('$_news/$id', data: form);
    return News.fromJson((response.data as Map).cast<String, dynamic>());
  }

  Future<void> delete(int id) async {
    await _dio.delete<dynamic>('$_news/$id');
  }
}
