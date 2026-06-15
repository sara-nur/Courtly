import '../theme/app_colors.dart';

/// Mirrors the backend `ReservationStatus` enum (int values 0..3).
/// Drives the reservation state machine UI and status badges.
enum ReservationStatus {
  pending,
  confirmed,
  cancelled,
  completed;

  /// Integer value matching the backend enum.
  int get wireValue => index;

  /// Parses the backend integer; falls back to [pending] when unknown.
  static ReservationStatus fromWire(int value) =>
      (value >= 0 && value < values.length) ? values[value] : pending;

  String get label => switch (this) {
        ReservationStatus.pending => 'Pending',
        ReservationStatus.confirmed => 'Confirmed',
        ReservationStatus.cancelled => 'Cancelled',
        ReservationStatus.completed => 'Completed',
      };

  StatusTone get tone => switch (this) {
        ReservationStatus.pending => StatusTone.warning,
        ReservationStatus.confirmed => StatusTone.success,
        ReservationStatus.cancelled => StatusTone.danger,
        ReservationStatus.completed => StatusTone.info,
      };
}
