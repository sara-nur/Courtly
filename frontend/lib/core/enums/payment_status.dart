import '../theme/app_colors.dart';

/// Mirrors the backend `PaymentStatus` enum (int values 0..3).
enum PaymentStatus {
  pending,
  succeeded,
  failed,
  refunded;

  int get wireValue => index;

  static PaymentStatus fromWire(int value) =>
      (value >= 0 && value < values.length) ? values[value] : pending;

  String get label => switch (this) {
        PaymentStatus.pending => 'Pending',
        PaymentStatus.succeeded => 'Paid',
        PaymentStatus.failed => 'Failed',
        PaymentStatus.refunded => 'Refunded',
      };

  StatusTone get tone => switch (this) {
        PaymentStatus.pending => StatusTone.warning,
        PaymentStatus.succeeded => StatusTone.success,
        PaymentStatus.failed => StatusTone.danger,
        PaymentStatus.refunded => StatusTone.neutral,
      };
}
