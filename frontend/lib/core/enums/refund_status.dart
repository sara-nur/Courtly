import '../theme/app_colors.dart';

/// Mirrors the backend `RefundStatus` enum (int values 0..2).
enum RefundStatus {
  pending,
  succeeded,
  failed;

  int get wireValue => index;

  static RefundStatus fromWire(int value) =>
      (value >= 0 && value < values.length) ? values[value] : pending;

  String get label => switch (this) {
        RefundStatus.pending => 'Pending',
        RefundStatus.succeeded => 'Refunded',
        RefundStatus.failed => 'Failed',
      };

  StatusTone get tone => switch (this) {
        RefundStatus.pending => StatusTone.warning,
        RefundStatus.succeeded => StatusTone.success,
        RefundStatus.failed => StatusTone.danger,
      };
}
