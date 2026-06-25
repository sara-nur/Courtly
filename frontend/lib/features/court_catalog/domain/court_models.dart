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

import '../../../core/enums/maintenance_status.dart';

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
/// Feature 12 adds the derived maintenance state ([isUnderMaintenance] +
/// [maintenanceReason]/[maintenanceStartUtc]) so the grid card can show a
/// Maintenance badge and "Unavailable for reservations". These are read-only
/// projections of the court's OPEN maintenance window; the windows themselves
/// ([CourtMaintenanceLog]) are managed via the maintenance modal.
///
/// Remaining deferred fields (popularity, rating) are intentionally NOT modeled
/// here — they belong to later features.
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
    this.isUnderMaintenance = false,
    this.maintenanceReason,
    this.maintenanceStartUtc,
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

  /// True when the court has an OPEN maintenance window covering now (F12) — it
  /// is then unavailable for reservations and excluded from analytics.
  final bool isUnderMaintenance;

  /// Reason of the active maintenance window (null when not under maintenance).
  final String? maintenanceReason;

  /// When the active maintenance window started (null when not under maintenance).
  final DateTime? maintenanceStartUtc;

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
        isUnderMaintenance: json['isUnderMaintenance'] as bool? ?? false,
        maintenanceReason: json['maintenanceReason'] as String?,
        maintenanceStartUtc: json['maintenanceStartUtc'] == null
            ? null
            : DateTime.parse(json['maintenanceStartUtc'] as String),
      );
}

/// One court maintenance WINDOW (feature 12) — a row in the court's status
/// history. Mirrors the backend `CourtMaintenanceLogDto`: a [status] that moves
/// through the maintenance state machine, the [reason], the [startUtc]/[endUtc]
/// window, and the resolved [performedByName] (never a raw user id) for the
/// "who/when/why" history.
class CourtMaintenanceLog {
  const CourtMaintenanceLog({
    required this.id,
    required this.courtId,
    required this.status,
    required this.reason,
    required this.startUtc,
    this.endUtc,
    this.performedByName,
    required this.createdAtUtc,
  });

  final int id;
  final int courtId;
  final MaintenanceStatus status;
  final String reason;
  final DateTime startUtc;
  final DateTime? endUtc;
  final String? performedByName;
  final DateTime createdAtUtc;

  factory CourtMaintenanceLog.fromJson(Map<String, dynamic> json) =>
      CourtMaintenanceLog(
        id: (json['id'] as num).toInt(),
        courtId: (json['courtId'] as num?)?.toInt() ?? 0,
        status: MaintenanceStatus.fromWire((json['status'] as num).toInt()),
        reason: json['reason'] as String? ?? '',
        startUtc: DateTime.parse(json['startUtc'] as String),
        endUtc: json['endUtc'] == null
            ? null
            : DateTime.parse(json['endUtc'] as String),
        performedByName: json['performedByName'] as String?,
        createdAtUtc: DateTime.parse(json['createdAtUtc'] as String),
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
