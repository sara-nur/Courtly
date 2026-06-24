import 'package:dio/dio.dart';

import '../domain/court_models.dart';

/// Thin transport over the Feature 10 court endpoints. It only shapes requests
/// (path, query params, JSON bodies) and parses responses into [Court];
/// failures propagate as [DioException] and the repository normalizes them to a
/// typed `ApiException`.
///
/// Endpoints (must match the backend exactly):
///   GET        /api/courts            (paged + filters)
///   POST       /api/courts
///   GET        /api/courts/{id}
///   PUT/DELETE /api/courts/{id}
class CourtApi {
  CourtApi(this._dio);

  final Dio _dio;

  static const String _courts = '/api/courts';

  /// Builds the list query, omitting any null/empty filter so the backend only
  /// applies the filters the UI actually set.
  Map<String, dynamic> _listQuery({
    required int page,
    required int pageSize,
    String? search,
    int? cityId,
    int? countryId,
    int? surfaceTypeId,
    int? courtTypeId,
    bool? isIndoor,
    bool? isActive,
    bool? isFeatured,
    double? minPrice,
    double? maxPrice,
  }) {
    final query = <String, dynamic>{'page': page, 'pageSize': pageSize};
    if (search != null && search.trim().isNotEmpty) {
      query['search'] = search.trim();
    }
    if (cityId != null) query['cityId'] = cityId;
    if (countryId != null) query['countryId'] = countryId;
    if (surfaceTypeId != null) query['surfaceTypeId'] = surfaceTypeId;
    if (courtTypeId != null) query['courtTypeId'] = courtTypeId;
    if (isIndoor != null) query['isIndoor'] = isIndoor;
    if (isActive != null) query['isActive'] = isActive;
    if (isFeatured != null) query['isFeatured'] = isFeatured;
    if (minPrice != null) query['minPrice'] = minPrice;
    if (maxPrice != null) query['maxPrice'] = maxPrice;
    return query;
  }

  Future<PagedResult<Court>> list({
    int page = 1,
    int pageSize = 20,
    String? search,
    int? cityId,
    int? countryId,
    int? surfaceTypeId,
    int? courtTypeId,
    bool? isIndoor,
    bool? isActive,
    bool? isFeatured,
    double? minPrice,
    double? maxPrice,
  }) async {
    final response = await _dio.get<dynamic>(
      _courts,
      queryParameters: _listQuery(
        page: page,
        pageSize: pageSize,
        search: search,
        cityId: cityId,
        countryId: countryId,
        surfaceTypeId: surfaceTypeId,
        courtTypeId: courtTypeId,
        isIndoor: isIndoor,
        isActive: isActive,
        isFeatured: isFeatured,
        minPrice: minPrice,
        maxPrice: maxPrice,
      ),
    );
    return PagedResult<Court>.fromJson(
      (response.data as Map).cast<String, dynamic>(),
      Court.fromJson,
    );
  }

  Future<Court> getById(int id) async {
    final response = await _dio.get<dynamic>('$_courts/$id');
    return Court.fromJson((response.data as Map).cast<String, dynamic>());
  }

  Future<Court> create(Map<String, dynamic> payload) async {
    final response = await _dio.post<dynamic>(_courts, data: payload);
    return Court.fromJson((response.data as Map).cast<String, dynamic>());
  }

  Future<Court> update(int id, Map<String, dynamic> payload) async {
    final response = await _dio.put<dynamic>('$_courts/$id', data: payload);
    return Court.fromJson((response.data as Map).cast<String, dynamic>());
  }

  Future<void> delete(int id) async {
    await _dio.delete<dynamic>('$_courts/$id');
  }
}
