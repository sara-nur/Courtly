/// Immutable domain model for Feature 10 (court catalog admin CRUD).
///
/// Mirrors the backend CourtDto with **camelCase** JSON keys. FK ids are kept
/// for API calls (PUT/DELETE `/api/courts/{id}`) but are **never shown in the
/// UI** (rubric): cards and dropdowns render names, so a [Court] surfaces its
/// [cityName]/[surfaceTypeName]/[courtTypeName] rather than the raw FK ids.
///
/// The shared [PagedResult] is reused from the reference_data feature (there is
/// no `core/` copy) to avoid a DRY violation — do not redefine it here.
library;

export '../../reference_data/domain/reference_models.dart' show PagedResult;

/// A bookable court. FK ids travel with the payload for create/update; the UI
/// renders the projected names ([cityName], [surfaceTypeName], [courtTypeName]).
///
/// Feature 11 adds the map location ([latitude]/[longitude]) and populates
/// [primaryImageUrl] (a RELATIVE `/api/images/{id}` path the UI prefixes with
/// the configured base URL via `absoluteImageUrl`). Court images and amenity
/// links are sub-resources modeled separately ([CourtImage], [CourtAmenityLink])
/// and loaded by the form on demand.
///
/// Remaining deferred fields (maintenance, popularity, rating) are intentionally
/// NOT modeled here — they belong to later features.
class Court {
  const Court({
    required this.id,
    required this.name,
    this.description,
    required this.cityId,
    required this.cityName,
    required this.countryId,
    required this.countryName,
    required this.surfaceTypeId,
    required this.surfaceTypeName,
    required this.courtTypeId,
    required this.courtTypeName,
    required this.isIndoor,
    required this.isActive,
    required this.isFeatured,
    required this.hourlyPrice,
    this.latitude,
    this.longitude,
    this.primaryImageUrl,
  });

  final int id;
  final String name;
  final String? description;
  final int cityId;
  final String cityName;
  final int countryId;
  final String countryName;
  final int surfaceTypeId;
  final String surfaceTypeName;
  final int courtTypeId;
  final String courtTypeName;
  final bool isIndoor;
  final bool isActive;
  final bool isFeatured;

  /// Backend `decimal` → arrives as a JSON number; render with
  /// `Formatters.money(...)` (NOT `moneyFromCents`).
  final double hourlyPrice;

  /// Map location (both-or-neither). Null when the court has no pinned point.
  final double? latitude;
  final double? longitude;

  /// Nullable already-seeded image URL; falls back to a placeholder icon in the
  /// grid card when null/empty. RELATIVE (`/api/images/{id}`) — prefix with the
  /// configured base URL via `absoluteImageUrl` before display.
  final String? primaryImageUrl;

  factory Court.fromJson(Map<String, dynamic> json) => Court(
        id: (json['id'] as num).toInt(),
        name: json['name'] as String,
        description: json['description'] as String?,
        cityId: (json['cityId'] as num).toInt(),
        cityName: json['cityName'] as String? ?? '',
        countryId: (json['countryId'] as num?)?.toInt() ?? 0,
        countryName: json['countryName'] as String? ?? '',
        surfaceTypeId: (json['surfaceTypeId'] as num).toInt(),
        surfaceTypeName: json['surfaceTypeName'] as String? ?? '',
        courtTypeId: (json['courtTypeId'] as num).toInt(),
        courtTypeName: json['courtTypeName'] as String? ?? '',
        isIndoor: json['isIndoor'] as bool? ?? false,
        isActive: json['isActive'] as bool? ?? false,
        isFeatured: json['isFeatured'] as bool? ?? false,
        hourlyPrice: (json['hourlyPrice'] as num).toDouble(),
        latitude: (json['latitude'] as num?)?.toDouble(),
        longitude: (json['longitude'] as num?)?.toDouble(),
        primaryImageUrl: json['primaryImageUrl'] as String?,
      );
}

/// One uploaded court image (sub-resource of a court). [url] is RELATIVE
/// (`/api/images/{id}`); compose the absolute URL with `absoluteImageUrl` and
/// the configured base URL before fetching. Exactly one image per court is the
/// primary one ([isPrimary]).
class CourtImage {
  const CourtImage({
    required this.id,
    required this.courtId,
    required this.url,
    required this.isPrimary,
    this.caption,
  });

  final int id;
  final int courtId;
  final String url;
  final bool isPrimary;
  final String? caption;

  factory CourtImage.fromJson(Map<String, dynamic> json) => CourtImage(
        id: (json['id'] as num).toInt(),
        courtId: (json['courtId'] as num?)?.toInt() ?? 0,
        url: json['url'] as String? ?? '',
        isPrimary: json['isPrimary'] as bool? ?? false,
        caption: json['caption'] as String?,
      );
}

/// A court↔amenity link (M:N with a payload). Renders the amenity [amenityName]
/// (never the raw [amenityId]); [note] and [isHighlighted] are per-court extras.
class CourtAmenityLink {
  const CourtAmenityLink({
    required this.id,
    required this.courtId,
    required this.amenityId,
    required this.amenityName,
    this.iconKey,
    this.note,
    required this.isHighlighted,
  });

  final int id;
  final int courtId;
  final int amenityId;
  final String amenityName;
  final String? iconKey;
  final String? note;
  final bool isHighlighted;

  factory CourtAmenityLink.fromJson(Map<String, dynamic> json) =>
      CourtAmenityLink(
        id: (json['id'] as num?)?.toInt() ?? 0,
        courtId: (json['courtId'] as num?)?.toInt() ?? 0,
        amenityId: (json['amenityId'] as num).toInt(),
        amenityName: json['amenityName'] as String? ?? '',
        iconKey: json['iconKey'] as String?,
        note: json['note'] as String?,
        isHighlighted: json['isHighlighted'] as bool? ?? false,
      );
}
