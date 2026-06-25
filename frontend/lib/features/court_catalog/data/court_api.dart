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
///
/// Feature 11 sub-resources (court images + amenity links). List responses are
/// raw JSON arrays (NOT a [PagedResult] envelope):
///   GET    /api/courts/{id}/images
///   POST   /api/courts/{id}/images                (multipart: file, caption, isPrimary)
///   PUT    /api/courts/{id}/images/{imageId}/primary
///   DELETE /api/courts/{id}/images/{imageId}
///   GET    /api/courts/{id}/amenities
///   PUT    /api/courts/{id}/amenities             (replaces the whole set)
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

  // --- Court images ----------------------------------------------------------

  /// Parses a raw JSON array response into a typed list (sub-resource lists are
  /// not paged — see the class doc).
  List<T> _parseList<T>(
    dynamic data,
    T Function(Map<String, dynamic>) fromJson,
  ) {
    final raw = (data as List?) ?? const <dynamic>[];
    return raw
        .map((e) => fromJson((e as Map).cast<String, dynamic>()))
        .toList(growable: false);
  }

  Future<List<CourtImage>> listImages(int courtId) async {
    final response = await _dio.get<dynamic>('$_courts/$courtId/images');
    return _parseList(response.data, CourtImage.fromJson);
  }

  Future<CourtImage> uploadImage(
    int courtId, {
    required List<int> bytes,
    required String filename,
    String? caption,
    bool isPrimary = false,
  }) async {
    final form = FormData.fromMap({
      'file': MultipartFile.fromBytes(bytes, filename: filename),
      if (caption != null) 'caption': caption,
      'isPrimary': isPrimary,
    });
    final response =
        await _dio.post<dynamic>('$_courts/$courtId/images', data: form);
    return CourtImage.fromJson((response.data as Map).cast<String, dynamic>());
  }

  Future<CourtImage> setPrimaryImage(int courtId, int imageId) async {
    final response = await _dio
        .put<dynamic>('$_courts/$courtId/images/$imageId/primary');
    return CourtImage.fromJson((response.data as Map).cast<String, dynamic>());
  }

  Future<void> deleteImage(int courtId, int imageId) async {
    await _dio.delete<dynamic>('$_courts/$courtId/images/$imageId');
  }

  // --- Court amenities -------------------------------------------------------

  Future<List<CourtAmenityLink>> listAmenities(int courtId) async {
    final response = await _dio.get<dynamic>('$_courts/$courtId/amenities');
    return _parseList(response.data, CourtAmenityLink.fromJson);
  }

  /// REPLACES the court's whole amenity set with [items].
  Future<List<CourtAmenityLink>> setAmenities(
    int courtId,
    List<({int amenityId, String? note, bool isHighlighted})> items,
  ) async {
    final body = <String, dynamic>{
      'amenities': items
          .map((e) => <String, dynamic>{
                'amenityId': e.amenityId,
                'note': e.note,
                'isHighlighted': e.isHighlighted,
              })
          .toList(),
    };
    final response =
        await _dio.put<dynamic>('$_courts/$courtId/amenities', data: body);
    return _parseList(response.data, CourtAmenityLink.fromJson);
  }
}
