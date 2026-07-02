/// Immutable domain model for Feature 15A (Users master-detail admin UI).
///
/// Mirrors the backend `UserDetailDto` (camelCase JSON keys). Renders name /
/// email / phone / city / roles / active state (never a raw id — rubric §6). The
/// [id] is a `Guid` string kept only for API calls (activate/deactivate, role
/// assignment, and to reuse the reservations list keyed by `userId`).
library;

/// The full user view for the master-detail screen: profile, roles and the
/// active flag. Times arrive UTC; render with `.toLocal()` via `Formatters`.
class UserDetail {
  const UserDetail({
    required this.id,
    required this.firstName,
    required this.lastName,
    required this.fullName,
    this.email,
    this.phoneNumber,
    this.cityName,
    required this.roles,
    required this.isActive,
    required this.createdAtUtc,
    this.isProtected = false,
  });

  /// Backend `Guid` → string id (used only for API calls, never shown).
  final String id;
  final String firstName;
  final String lastName;
  final String fullName;
  final String? email;
  final String? phoneNumber;
  final String? cityName;
  final List<String> roles;
  final bool isActive;
  final DateTime createdAtUtc;

  /// True for the seeded super administrator, which the backend refuses to
  /// deactivate or re-role; the detail screen disables those actions.
  final bool isProtected;

  factory UserDetail.fromJson(Map<String, dynamic> json) => UserDetail(
        id: json['id'] as String? ?? '',
        firstName: json['firstName'] as String? ?? '',
        lastName: json['lastName'] as String? ?? '',
        fullName: json['fullName'] as String? ?? '',
        email: json['email'] as String?,
        phoneNumber: json['phoneNumber'] as String?,
        cityName: json['cityName'] as String?,
        roles: ((json['roles'] as List?) ?? const <dynamic>[])
            .map((e) => e.toString())
            .toList(growable: false),
        isActive: json['isActive'] as bool? ?? false,
        createdAtUtc: DateTime.parse(json['createdAtUtc'] as String),
        isProtected: json['isProtected'] as bool? ?? false,
      );
}
