import '../theme/app_colors.dart';

/// Mirrors the backend `NotificationType` enum (int values 0..7). Declaration
/// order must match the backend int values so `fromWire(index)` resolves
/// correctly — `reservationRescheduled` is 7, appended after `general` (6).
enum NotificationType {
  reservationCreated,
  reservationConfirmed,
  reservationCancelled,
  reservationCompleted,
  paymentSucceeded,
  paymentRefunded,
  general,
  reservationRescheduled;

  int get wireValue => index;

  static NotificationType fromWire(int value) =>
      (value >= 0 && value < values.length) ? values[value] : general;

  String get label => switch (this) {
        NotificationType.reservationCreated => 'Reservation created',
        NotificationType.reservationConfirmed => 'Reservation confirmed',
        NotificationType.reservationCancelled => 'Reservation cancelled',
        NotificationType.reservationRescheduled => 'Reservation rescheduled',
        NotificationType.reservationCompleted => 'Reservation completed',
        NotificationType.paymentSucceeded => 'Payment received',
        NotificationType.paymentRefunded => 'Payment refunded',
        NotificationType.general => 'Notification',
      };

  StatusTone get tone => switch (this) {
        NotificationType.reservationConfirmed ||
        NotificationType.paymentSucceeded =>
          StatusTone.success,
        NotificationType.reservationCancelled => StatusTone.danger,
        NotificationType.paymentRefunded => StatusTone.neutral,
        _ => StatusTone.info,
      };
}
