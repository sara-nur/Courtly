// F27 — Client notifications UI. Shared visual mapping for notifications so the
// list tile and the detail screen render the same leading icon per type.
import 'package:flutter/material.dart';

import '../../../core/enums/enums.dart';

/// Leading icon per notification type — reservation lifecycle vs. payment vs.
/// generic. The colour comes from `type.tone` at the call site.
IconData iconForNotificationType(NotificationType type) => switch (type) {
      NotificationType.reservationCreated => Icons.event_available,
      NotificationType.reservationConfirmed => Icons.check_circle_outline,
      NotificationType.reservationCancelled => Icons.event_busy,
      NotificationType.reservationCompleted => Icons.task_alt,
      NotificationType.paymentSucceeded => Icons.payments_outlined,
      NotificationType.paymentRefunded => Icons.undo,
      NotificationType.general => Icons.notifications_none,
    };
