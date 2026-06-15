import '../theme/app_colors.dart';

/// Mirrors the backend `MaintenanceStatus` enum (int values 0..3).
enum MaintenanceStatus {
  scheduled,
  inProgress,
  completed,
  cancelled;

  int get wireValue => index;

  static MaintenanceStatus fromWire(int value) =>
      (value >= 0 && value < values.length) ? values[value] : scheduled;

  String get label => switch (this) {
        MaintenanceStatus.scheduled => 'Scheduled',
        MaintenanceStatus.inProgress => 'In Progress',
        MaintenanceStatus.completed => 'Completed',
        MaintenanceStatus.cancelled => 'Cancelled',
      };

  StatusTone get tone => switch (this) {
        MaintenanceStatus.scheduled => StatusTone.info,
        MaintenanceStatus.inProgress => StatusTone.warning,
        MaintenanceStatus.completed => StatusTone.success,
        MaintenanceStatus.cancelled => StatusTone.neutral,
      };
}
