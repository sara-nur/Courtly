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
/// Deferred fields (images upload, lat/lng, amenities, maintenance, popularity,
/// rating) are intentionally NOT modeled here — they belong to later features.
/// Only [primaryImageUrl] is read, to DISPLAY an already-seeded image.
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

  /// Nullable already-seeded image URL; falls back to a placeholder icon in the
  /// grid card when null/empty.
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
        primaryImageUrl: json['primaryImageUrl'] as String?,
      );
}
