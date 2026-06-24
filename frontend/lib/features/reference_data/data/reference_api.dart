import 'package:dio/dio.dart';

import '../domain/reference_models.dart';

/// Thin transport over the Feature 9 reference-data endpoints. It only shapes
/// requests (paths, query params, JSON bodies) and parses responses into the
/// domain models; failures propagate as [DioException] and the repository
/// normalizes them to a typed `ApiException`.
///
/// Endpoints (must match the backend exactly):
///   GET/POST   /api/countries, /api/cities, /api/surface-types,
///              /api/court-types, /api/amenities
///   GET        /api/{plural}/{id}
///   PUT/DELETE /api/{plural}/{id}
///   GET        /api/countries/lookup   (full cached country list for the City form)
class ReferenceApi {
  ReferenceApi(this._dio);

  final Dio _dio;

  static const String _countries = '/api/countries';
  static const String _cities = '/api/cities';
  static const String _surfaceTypes = '/api/surface-types';
  static const String _courtTypes = '/api/court-types';
  static const String _amenities = '/api/amenities';

  // --- Shared helpers --------------------------------------------------------

  /// Builds the `{ page, pageSize, search }` query, omitting a blank search.
  Map<String, dynamic> _listQuery(int page, int pageSize, String? search) {
    final query = <String, dynamic>{'page': page, 'pageSize': pageSize};
    if (search != null && search.trim().isNotEmpty) {
      query['search'] = search.trim();
    }
    return query;
  }

  Future<PagedResult<T>> _list<T>(
    String path, {
    required int page,
    required int pageSize,
    required String? search,
    required T Function(Map<String, dynamic>) fromJson,
  }) async {
    final response = await _dio.get<dynamic>(
      path,
      queryParameters: _listQuery(page, pageSize, search),
    );
    return PagedResult<T>.fromJson(
      (response.data as Map).cast<String, dynamic>(),
      fromJson,
    );
  }

  Future<T> _create<T>(
    String path,
    Map<String, dynamic> payload,
    T Function(Map<String, dynamic>) fromJson,
  ) async {
    final response = await _dio.post<dynamic>(path, data: payload);
    return fromJson((response.data as Map).cast<String, dynamic>());
  }

  Future<T> _update<T>(
    String path,
    int id,
    Map<String, dynamic> payload,
    T Function(Map<String, dynamic>) fromJson,
  ) async {
    final response = await _dio.put<dynamic>('$path/$id', data: payload);
    return fromJson((response.data as Map).cast<String, dynamic>());
  }

  Future<void> _delete(String path, int id) async {
    await _dio.delete<dynamic>('$path/$id');
  }

  // --- Countries -------------------------------------------------------------

  Future<PagedResult<Country>> listCountries({
    int page = 1,
    int pageSize = 20,
    String? search,
  }) =>
      _list(_countries,
          page: page,
          pageSize: pageSize,
          search: search,
          fromJson: Country.fromJson);

  Future<Country> createCountry(Map<String, dynamic> payload) =>
      _create(_countries, payload, Country.fromJson);

  Future<Country> updateCountry(int id, Map<String, dynamic> payload) =>
      _update(_countries, id, payload, Country.fromJson);

  Future<void> deleteCountry(int id) => _delete(_countries, id);

  /// Full, server-cached country list for the City form dropdown + the City
  /// "empty prerequisite" check.
  Future<List<Country>> lookupCountries() async {
    final response = await _dio.get<dynamic>('$_countries/lookup');
    final raw = (response.data as List?) ?? const <dynamic>[];
    return raw
        .map((e) => Country.fromJson((e as Map).cast<String, dynamic>()))
        .toList(growable: false);
  }

  // --- Cities ----------------------------------------------------------------

  Future<PagedResult<City>> listCities({
    int page = 1,
    int pageSize = 20,
    String? search,
  }) =>
      _list(_cities,
          page: page,
          pageSize: pageSize,
          search: search,
          fromJson: City.fromJson);

  Future<City> createCity(Map<String, dynamic> payload) =>
      _create(_cities, payload, City.fromJson);

  Future<City> updateCity(int id, Map<String, dynamic> payload) =>
      _update(_cities, id, payload, City.fromJson);

  Future<void> deleteCity(int id) => _delete(_cities, id);

  // --- Surface types ---------------------------------------------------------

  Future<PagedResult<SurfaceType>> listSurfaceTypes({
    int page = 1,
    int pageSize = 20,
    String? search,
  }) =>
      _list(_surfaceTypes,
          page: page,
          pageSize: pageSize,
          search: search,
          fromJson: SurfaceType.fromJson);

  Future<SurfaceType> createSurfaceType(Map<String, dynamic> payload) =>
      _create(_surfaceTypes, payload, SurfaceType.fromJson);

  Future<SurfaceType> updateSurfaceType(int id, Map<String, dynamic> payload) =>
      _update(_surfaceTypes, id, payload, SurfaceType.fromJson);

  Future<void> deleteSurfaceType(int id) => _delete(_surfaceTypes, id);

  // --- Court types -----------------------------------------------------------

  Future<PagedResult<CourtType>> listCourtTypes({
    int page = 1,
    int pageSize = 20,
    String? search,
  }) =>
      _list(_courtTypes,
          page: page,
          pageSize: pageSize,
          search: search,
          fromJson: CourtType.fromJson);

  Future<CourtType> createCourtType(Map<String, dynamic> payload) =>
      _create(_courtTypes, payload, CourtType.fromJson);

  Future<CourtType> updateCourtType(int id, Map<String, dynamic> payload) =>
      _update(_courtTypes, id, payload, CourtType.fromJson);

  Future<void> deleteCourtType(int id) => _delete(_courtTypes, id);

  // --- Amenities -------------------------------------------------------------

  Future<PagedResult<Amenity>> listAmenities({
    int page = 1,
    int pageSize = 20,
    String? search,
  }) =>
      _list(_amenities,
          page: page,
          pageSize: pageSize,
          search: search,
          fromJson: Amenity.fromJson);

  Future<Amenity> createAmenity(Map<String, dynamic> payload) =>
      _create(_amenities, payload, Amenity.fromJson);

  Future<Amenity> updateAmenity(int id, Map<String, dynamic> payload) =>
      _update(_amenities, id, payload, Amenity.fromJson);

  Future<void> deleteAmenity(int id) => _delete(_amenities, id);
}
