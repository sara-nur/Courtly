import 'package:flutter/material.dart';

import '../../../../core/theme/app_colors.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../core/utils/formatters.dart';
import '../../../court_catalog/domain/court_models.dart';

/// A free-slot chooser grouped Morning / Afternoon / Evening, shared by the
/// "+ New Booking" and Reschedule modals. Only free slots are offered (taken
/// slots render nowhere — the booking grid is server-authoritative); an optional
/// [excludeSlotId] hides the reservation's current slot when rescheduling. Each
/// chip shows the local start time + the server-owned price.
class FreeSlotPicker extends StatelessWidget {
  const FreeSlotPicker({
    super.key,
    required this.day,
    required this.selectedSlotId,
    required this.onSelected,
    this.excludeSlotId,
  });

  final DayAvailability day;
  final int? selectedSlotId;
  final ValueChanged<int> onSelected;
  final int? excludeSlotId;

  @override
  Widget build(BuildContext context) {
    if (day.isCourtUnderMaintenance) {
      return const _Empty(message: 'This court is under maintenance on this day.');
    }

    final groups = <Widget>[];
    for (final bucket in day.buckets) {
      final free = bucket.slots
          .where((s) => !s.isTaken && s.id != excludeSlotId)
          .toList(growable: false);
      if (free.isEmpty) continue;
      groups.add(Padding(
        padding: const EdgeInsets.only(top: AppSpacing.sm),
        child: Align(
          alignment: Alignment.centerLeft,
          child: Text(bucket.bucketName,
              style: Theme.of(context).textTheme.labelLarge),
        ),
      ));
      groups.add(const SizedBox(height: AppSpacing.xs));
      groups.add(Wrap(
        spacing: AppSpacing.xs,
        runSpacing: AppSpacing.xs,
        children: [
          for (final slot in free)
            ChoiceChip(
              label: Text(
                  '${Formatters.time(slot.startUtc.toLocal())} · ${Formatters.money(slot.price)}'),
              selected: selectedSlotId == slot.id,
              onSelected: (_) => onSelected(slot.id),
            ),
        ],
      ));
    }

    if (groups.isEmpty) {
      return const _Empty(message: 'No free slots on this day. Try another date.');
    }

    return SingleChildScrollView(
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: groups),
    );
  }
}

class _Empty extends StatelessWidget {
  const _Empty({required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Text(
        message,
        style: Theme.of(context)
            .textTheme
            .bodyMedium
            ?.copyWith(color: AppColors.textSecondary),
      ),
    );
  }
}
