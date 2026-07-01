import 'package:flutter/material.dart';

import '../../../../core/enums/time_of_day_bucket.dart';
import '../../../../core/theme/app_colors.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../core/utils/formatters.dart';
import '../../../court_catalog/domain/court_models.dart';

/// The client booking slot picker (mockup p.9 §3.2.3): a day's [DayAvailability]
/// rendered as time-of-day sections (Morning / Afternoon / Evening), each a grid
/// of 3-column slot **cards**. Unlike the admin [FreeSlotPicker] (which hides
/// taken slots), the client shows every slot so the user sees why one is
/// unavailable: a **Taken** slot is disabled+labelled, a **Past** slot (for today)
/// is disabled, a free slot is selectable, and the picked one shows **Selected**.
/// Selection state and the price live on the parent screen — this widget only
/// renders and reports taps for bookable slots.
class SlotGrid extends StatelessWidget {
  const SlotGrid({
    super.key,
    required this.day,
    required this.selectedSlotId,
    required this.onSelected,
    required this.now,
  });

  final DayAvailability day;
  final int? selectedSlotId;
  final ValueChanged<AvailabilitySlot> onSelected;

  /// The current instant, used to disable slots that already started today.
  /// Passed in (not read inside `build`) so widget tests are deterministic.
  final DateTime now;

  static const int _columns = 3;

  @override
  Widget build(BuildContext context) {
    if (day.isCourtUnderMaintenance) {
      return const _SlotGridMessage(
        icon: Icons.build_outlined,
        message: 'This court is under maintenance on this day. Please pick another date.',
      );
    }

    final sections = day.buckets.where((b) => b.slots.isNotEmpty).toList(growable: false);
    if (sections.isEmpty) {
      return const _SlotGridMessage(
        icon: Icons.event_busy_outlined,
        message: 'No time slots for this day. Try another date.',
      );
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        for (final bucket in sections) ...[
          _BucketHeader(bucket: bucket.bucket, name: bucket.bucketName),
          const SizedBox(height: AppSpacing.sm),
          LayoutBuilder(
            builder: (context, constraints) {
              final cardWidth =
                  (constraints.maxWidth - AppSpacing.sm * (_columns - 1)) / _columns;
              return Wrap(
                spacing: AppSpacing.sm,
                runSpacing: AppSpacing.sm,
                children: [
                  for (final slot in bucket.slots)
                    SizedBox(
                      width: cardWidth,
                      child: _SlotCard(
                        slot: slot,
                        isSelected: slot.id == selectedSlotId,
                        isPast: slot.startUtc.toLocal().isBefore(now),
                        onSelected: onSelected,
                      ),
                    ),
                ],
              );
            },
          ),
          const SizedBox(height: AppSpacing.lg),
        ],
      ],
    );
  }
}

/// The "☀ Morning / Afternoon / 🌙 Evening" section label above a slot grid.
class _BucketHeader extends StatelessWidget {
  const _BucketHeader({required this.bucket, required this.name});

  final TimeOfDayBucket bucket;
  final String name;

  IconData get _icon => switch (bucket) {
        TimeOfDayBucket.morning => Icons.wb_sunny_outlined,
        TimeOfDayBucket.afternoon => Icons.wb_twilight,
        TimeOfDayBucket.evening => Icons.nightlight_outlined,
      };

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        Icon(_icon, size: 18, color: AppColors.textSecondary),
        const SizedBox(width: AppSpacing.xs),
        Text(name, style: Theme.of(context).textTheme.labelLarge),
      ],
    );
  }
}

/// One slot as a card. Taken/past slots are disabled (greyed, not tappable) with
/// a reason label; a free slot is tappable and the picked one is highlighted.
class _SlotCard extends StatelessWidget {
  const _SlotCard({
    required this.slot,
    required this.isSelected,
    required this.isPast,
    required this.onSelected,
  });

  final AvailabilitySlot slot;
  final bool isSelected;
  final bool isPast;
  final ValueChanged<AvailabilitySlot> onSelected;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final disabled = slot.isTaken || isPast;

    final (String label, Color accent) = switch ((disabled, slot.isTaken, isSelected)) {
      (true, true, _) => ('Taken', AppColors.textMuted),
      (true, false, _) => ('Past', AppColors.textMuted),
      (false, _, true) => ('Selected', AppColors.primary),
      _ => ('Available', AppColors.success),
    };

    final Color background;
    final Color border;
    final Color timeColor;
    if (disabled) {
      background = AppColors.surfaceMuted;
      border = Colors.transparent;
      timeColor = AppColors.textMuted;
    } else if (isSelected) {
      background = AppColors.primarySoft;
      border = AppColors.primary;
      timeColor = AppColors.primary;
    } else {
      background = AppColors.surface;
      border = AppColors.surfaceMuted;
      timeColor = AppColors.textPrimary;
    }

    return Semantics(
      button: !disabled,
      enabled: !disabled,
      selected: isSelected,
      label: '${Formatters.time(slot.startUtc.toLocal())} · $label',
      child: Material(
        color: background,
        borderRadius: AppSpacing.brMd,
        child: InkWell(
          borderRadius: AppSpacing.brMd,
          onTap: disabled ? null : () => onSelected(slot),
          child: Container(
            padding: const EdgeInsets.symmetric(
              vertical: AppSpacing.sm,
              horizontal: AppSpacing.xs,
            ),
            decoration: BoxDecoration(
              borderRadius: AppSpacing.brMd,
              border: Border.all(
                color: border,
                width: isSelected ? 2 : 1,
              ),
            ),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(
                  Formatters.time(slot.startUtc.toLocal()),
                  textAlign: TextAlign.center,
                  style: theme.textTheme.bodyMedium?.copyWith(
                    color: timeColor,
                    fontWeight: FontWeight.w700,
                  ),
                ),
                const SizedBox(height: AppSpacing.xxs),
                Row(
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [
                    Container(
                      width: 6,
                      height: 6,
                      decoration: BoxDecoration(color: accent, shape: BoxShape.circle),
                    ),
                    const SizedBox(width: AppSpacing.xxs),
                    Flexible(
                      child: Text(
                        label,
                        overflow: TextOverflow.ellipsis,
                        style: theme.textTheme.labelSmall?.copyWith(color: accent),
                      ),
                    ),
                  ],
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _SlotGridMessage extends StatelessWidget {
  const _SlotGridMessage({required this.icon, required this.message});

  final IconData icon;
  final String message;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.xl),
      child: Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(icon, color: AppColors.textMuted),
            const SizedBox(height: AppSpacing.xs),
            Text(
              message,
              textAlign: TextAlign.center,
              style: theme.textTheme.bodyMedium?.copyWith(color: AppColors.textSecondary),
            ),
          ],
        ),
      ),
    );
  }
}
