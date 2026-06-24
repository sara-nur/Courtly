/// Immutable domain models for Feature 9 (reference-data admin CRUD).
///
/// Each model mirrors its backend DTO with **camelCase** JSON keys. IDs are kept
/// for API calls (PUT/DELETE `/api/{plural}/{id}`) but are **never shown in the
/// UI** (rubric §6): lists and dropdowns render names, and a [City] surfaces its
/// [countryName] rather than a raw `countryId`.
library;

/// A country (e.g. France / FRA). `isoCode` is the 3-letter ISO 3166-1 alpha-3.
class Country {
  const Country({
    required this.id,
    required this.name,
    required this.isoCode,
  });

  final int id;
  final String name;
  final String isoCode;

  factory Country.fromJson(Map<String, dynamic> json) => Country(
        id: (json['id'] as num).toInt(),
        name: json['name'] as String,
        isoCode: json['isoCode'] as String? ?? '',
      );
}

/// A city belonging to a [Country]. The form selects the country via a
/// [DbDropdown] (FK by name, never a textbox) so [countryName] is shown and
/// [countryId] travels with the payload.
class City {
  const City({
    required this.id,
    required this.name,
    required this.countryId,
    required this.countryName,
  });

  final int id;
  final String name;
  final int countryId;
  final String countryName;

  factory City.fromJson(Map<String, dynamic> json) => City(
        id: (json['id'] as num).toInt(),
        name: json['name'] as String,
        countryId: (json['countryId'] as num).toInt(),
        countryName: json['countryName'] as String? ?? '',
      );
}

/// A court surface (Clay, Hard, Grass…) with an optional [description].
class SurfaceType {
  const SurfaceType({
    required this.id,
    required this.name,
    this.description,
  });

  final int id;
  final String name;
  final String? description;

  factory SurfaceType.fromJson(Map<String, dynamic> json) => SurfaceType(
        id: (json['id'] as num).toInt(),
        name: json['name'] as String,
        description: json['description'] as String?,
      );
}

/// A court category (Indoor, Outdoor, Padel…) with an optional [description].
class CourtType {
  const CourtType({
    required this.id,
    required this.name,
    this.description,
  });

  final int id;
  final String name;
  final String? description;

  factory CourtType.fromJson(Map<String, dynamic> json) => CourtType(
        id: (json['id'] as num).toInt(),
        name: json['name'] as String,
        description: json['description'] as String?,
      );
}

/// A bookable amenity (Lockers, Parking…) with an optional [iconKey] used by the
/// client app to render a glyph.
class Amenity {
  const Amenity({
    required this.id,
    required this.name,
    this.iconKey,
  });

  final int id;
  final String name;
  final String? iconKey;

  factory Amenity.fromJson(Map<String, dynamic> json) => Amenity(
        id: (json['id'] as num).toInt(),
        name: json['name'] as String,
        iconKey: json['iconKey'] as String?,
      );
}

/// A single page of [T] from a list endpoint. Mirrors the backend's paged
/// envelope and feeds [PaginatedListView] directly (page/pageSize/totalCount/
/// hasNext/hasPrevious). [itemBuilder] parses each raw row into [T].
class PagedResult<T> {
  const PagedResult({
    required this.items,
    required this.page,
    required this.pageSize,
    required this.totalCount,
    required this.hasNext,
    required this.hasPrevious,
  });

  final List<T> items;
  final int page;
  final int pageSize;
  final int totalCount;
  final bool hasNext;
  final bool hasPrevious;

  bool get isEmpty => items.isEmpty;

  factory PagedResult.fromJson(
    Map<String, dynamic> json,
    T Function(Map<String, dynamic>) itemBuilder,
  ) {
    final rawItems = (json['items'] as List?) ?? const <dynamic>[];
    return PagedResult<T>(
      items: rawItems
          .map((e) => itemBuilder((e as Map).cast<String, dynamic>()))
          .toList(growable: false),
      page: (json['page'] as num?)?.toInt() ?? 1,
      pageSize: (json['pageSize'] as num?)?.toInt() ?? rawItems.length,
      totalCount: (json['totalCount'] as num?)?.toInt() ?? rawItems.length,
      hasNext: json['hasNext'] as bool? ?? false,
      hasPrevious: json['hasPrevious'] as bool? ?? false,
    );
  }
}
