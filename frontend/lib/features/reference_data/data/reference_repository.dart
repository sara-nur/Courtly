import 'package:dio/dio.dart';

import '../../../core/network/api_exception.dart';
import '../domain/reference_models.dart';
import 'reference_api.dart';

/// Wraps [ReferenceApi] and normalizes every failure to a typed [ApiException]
/// (`try { … } on DioException catch (e) { throw ApiException.from(e); }`), so
/// the UI/controllers only ever see one error type — including the backend's
/// per-field validation messages and delete-restrict `BusinessException` text.
///
/// The method surface mirrors the API one-for-one, typed to the domain models.
class ReferenceRepository {
  ReferenceRepository(this._api);

  final ReferenceApi _api;

  // --- Countries -------------------------------------------------------------

  Future<PagedResult<Country>> listCountries({
    int page = 1,
    int pageSize = 20,
    String? search,
  }) async {
    try {
      return await _api.listCountries(
        page: page,
        pageSize: pageSize,
        search: search,
      );
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<Country> createCountry(Map<String, dynamic> payload) async {
    try {
      return await _api.createCountry(payload);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<Country> updateCountry(int id, Map<String, dynamic> payload) async {
    try {
      return await _api.updateCountry(id, payload);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<void> deleteCountry(int id) async {
    try {
      await _api.deleteCountry(id);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  /// Full, server-cached country list for the City form dropdown + the City
  /// "empty prerequisite" check.
  Future<List<Country>> lookupCountries() async {
    try {
      return await _api.lookupCountries();
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  // --- Cities ----------------------------------------------------------------

  Future<PagedResult<City>> listCities({
    int page = 1,
    int pageSize = 20,
    String? search,
  }) async {
    try {
      return await _api.listCities(
        page: page,
        pageSize: pageSize,
        search: search,
      );
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<City> createCity(Map<String, dynamic> payload) async {
    try {
      return await _api.createCity(payload);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<City> updateCity(int id, Map<String, dynamic> payload) async {
    try {
      return await _api.updateCity(id, payload);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<void> deleteCity(int id) async {
    try {
      await _api.deleteCity(id);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  // --- Surface types ---------------------------------------------------------

  Future<PagedResult<SurfaceType>> listSurfaceTypes({
    int page = 1,
    int pageSize = 20,
    String? search,
  }) async {
    try {
      return await _api.listSurfaceTypes(
        page: page,
        pageSize: pageSize,
        search: search,
      );
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<SurfaceType> createSurfaceType(Map<String, dynamic> payload) async {
    try {
      return await _api.createSurfaceType(payload);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<SurfaceType> updateSurfaceType(
    int id,
    Map<String, dynamic> payload,
  ) async {
    try {
      return await _api.updateSurfaceType(id, payload);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<void> deleteSurfaceType(int id) async {
    try {
      await _api.deleteSurfaceType(id);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  // --- Court types -----------------------------------------------------------

  Future<PagedResult<CourtType>> listCourtTypes({
    int page = 1,
    int pageSize = 20,
    String? search,
  }) async {
    try {
      return await _api.listCourtTypes(
        page: page,
        pageSize: pageSize,
        search: search,
      );
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<CourtType> createCourtType(Map<String, dynamic> payload) async {
    try {
      return await _api.createCourtType(payload);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<CourtType> updateCourtType(
    int id,
    Map<String, dynamic> payload,
  ) async {
    try {
      return await _api.updateCourtType(id, payload);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<void> deleteCourtType(int id) async {
    try {
      await _api.deleteCourtType(id);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  // --- Amenities -------------------------------------------------------------

  Future<PagedResult<Amenity>> listAmenities({
    int page = 1,
    int pageSize = 20,
    String? search,
  }) async {
    try {
      return await _api.listAmenities(
        page: page,
        pageSize: pageSize,
        search: search,
      );
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<Amenity> createAmenity(Map<String, dynamic> payload) async {
    try {
      return await _api.createAmenity(payload);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<Amenity> updateAmenity(int id, Map<String, dynamic> payload) async {
    try {
      return await _api.updateAmenity(id, payload);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }

  Future<void> deleteAmenity(int id) async {
    try {
      await _api.deleteAmenity(id);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }
}
