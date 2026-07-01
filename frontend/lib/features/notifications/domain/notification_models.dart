// F27 — Client notifications UI. Immutable domain model for a client notification.
///
/// [AppNotification] mirrors the backend `NotificationDto` (camelCase keys). It
/// is named `AppNotification` (not `Notification`) to avoid clashing with the
/// Flutter framework `Notification` type. Manual `fromJson` — the codebase
/// convention (no json_serializable). Timestamps are normalized to UTC.
library;

import '../../../core/enums/notification_type.dart';

/// One notification shown on the client notifications screen. [type] carries the
/// label + tone used for the tile badge; [isRead] drives the unread styling and
/// [readAtUtc] is set once the notification has been marked read.
class AppNotification {
  const AppNotification({
    required this.id,
    required this.type,
    required this.title,
    required this.text,
    required this.isRead,
    required this.createdAtUtc,
    this.readAtUtc,
  });

  final int id;
  final NotificationType type;
  final String title;
  final String text;
  final bool isRead;
  final DateTime createdAtUtc;
  final DateTime? readAtUtc;

  factory AppNotification.fromJson(Map<String, dynamic> json) =>
      AppNotification(
        id: (json['id'] as num).toInt(),
        type: NotificationType.fromWire((json['type'] as num?)?.toInt() ?? 6),
        title: json['title'] as String? ?? '',
        text: json['text'] as String? ?? '',
        isRead: json['isRead'] as bool? ?? false,
        createdAtUtc: DateTime.parse(json['createdAtUtc'] as String).toUtc(),
        readAtUtc: json['readAtUtc'] == null
            ? null
            : DateTime.parse(json['readAtUtc'] as String).toUtc(),
      );

  AppNotification copyWith({bool? isRead, DateTime? readAtUtc}) =>
      AppNotification(
        id: id,
        type: type,
        title: title,
        text: text,
        isRead: isRead ?? this.isRead,
        createdAtUtc: createdAtUtc,
        readAtUtc: readAtUtc ?? this.readAtUtc,
      );
}
