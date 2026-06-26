/// Minimal user model for Feature 15's "+ New Booking" customer picker.
///
/// Mirrors the backend `UserSummaryDto` (camelCase keys). Renders [fullName] +
/// [email] (never a raw id). Feature 15A grows this into the full Users feature
/// (detail + their reservations, activate/deactivate, role assignment).
library;

class UserSummary {
  const UserSummary({
    required this.id,
    required this.fullName,
    this.email,
    this.role,
    required this.isActive,
  });

  /// Backend `Guid` → string id (used only for the booking payload, never shown).
  final String id;
  final String fullName;
  final String? email;
  final String? role;
  final bool isActive;

  /// Dropdown label: name with the email as a disambiguator when present.
  String get pickerLabel => email == null || email!.isEmpty ? fullName : '$fullName ($email)';

  factory UserSummary.fromJson(Map<String, dynamic> json) => UserSummary(
        id: json['id'] as String? ?? '',
        fullName: json['fullName'] as String? ?? '',
        email: json['email'] as String?,
        role: json['role'] as String?,
        isActive: json['isActive'] as bool? ?? false,
      );
}
