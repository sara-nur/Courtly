// F27 — Client notifications UI. Full-screen view of a single notification,
// pushed above the shell when a list row is tapped. The notification travels as
// the route's `extra` (the list already holds the full record — status, title,
// text and timestamps — so there is no by-id refetch).
import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../../../core/widgets/widgets.dart';
import '../domain/notification_models.dart';
import 'notification_visuals.dart';

class NotificationDetailScreen extends StatelessWidget {
  const NotificationDetailScreen({super.key, required this.notification});

  final AppNotification? notification;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final n = notification;

    return Scaffold(
      appBar: AppBar(title: const Text('Notification')),
      body: n == null
          ? const _Unavailable()
          : ListView(
              padding: const EdgeInsets.all(AppSpacing.lg),
              children: [
                // Tone-coloured icon + the type badge — the "what kind" header.
                Row(
                  children: [
                    CircleAvatar(
                      radius: AppSpacing.avatarMd / 2,
                      backgroundColor: StatusToneColors.of(n.type.tone).background,
                      child: Icon(
                        iconForNotificationType(n.type),
                        color: StatusToneColors.of(n.type.tone).foreground,
                      ),
                    ),
                    const SizedBox(width: AppSpacing.md),
                    Expanded(
                      child: Align(
                        alignment: Alignment.centerLeft,
                        child: StatusBadge(label: n.type.label, tone: n.type.tone),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: AppSpacing.lg),
                Text(
                  n.title,
                  style: theme.textTheme.headlineSmall?.copyWith(
                    fontWeight: FontWeight.w700,
                    color: AppColors.textPrimary,
                  ),
                ),
                const SizedBox(height: AppSpacing.md),
                _MetaRow(icon: Icons.schedule, label: 'Received ${_formatFull(n.createdAtUtc)}'),
                if (n.readAtUtc != null) ...[
                  const SizedBox(height: AppSpacing.sm),
                  _MetaRow(icon: Icons.done_all, label: 'Read ${_formatFull(n.readAtUtc!)}'),
                ],
                const Padding(
                  padding: EdgeInsets.symmetric(vertical: AppSpacing.lg),
                  child: Divider(height: 1),
                ),
                SelectableText(
                  n.text,
                  style: theme.textTheme.bodyLarge?.copyWith(
                    color: AppColors.textPrimary,
                    height: 1.5,
                  ),
                ),
              ],
            ),
    );
  }

  /// Long, human date + time in the device's local zone.
  static String _formatFull(DateTime utc) =>
      DateFormat.yMMMMEEEEd().add_jm().format(utc.toLocal());
}

/// A muted icon + label line (timestamps).
class _MetaRow extends StatelessWidget {
  const _MetaRow({required this.icon, required this.label});

  final IconData icon;
  final String label;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Row(
      children: [
        Icon(icon, size: 16, color: AppColors.textMuted),
        const SizedBox(width: AppSpacing.xs),
        Expanded(
          child: Text(
            label,
            style: theme.textTheme.bodyMedium?.copyWith(color: AppColors.textSecondary),
          ),
        ),
      ],
    );
  }
}

/// Shown if the screen is reached without a notification (defensive).
class _Unavailable extends StatelessWidget {
  const _Unavailable();

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(AppSpacing.xl),
        child: Text(
          'This notification is no longer available.',
          style: Theme.of(context)
              .textTheme
              .bodyMedium
              ?.copyWith(color: AppColors.textSecondary),
        ),
      ),
    );
  }
}
