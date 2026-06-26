import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/theme/app_spacing.dart';
import '../../../../core/widgets/async_value_view.dart';
import '../../../../core/widgets/date_time_picker_field.dart';
import '../../../../core/widgets/form_scaffold.dart';
import '../../../court_catalog/application/court_providers.dart';
import '../../../court_catalog/domain/court_models.dart';
import '../widgets/free_slot_picker.dart';

/// Picks a new free slot to move a reservation to (same court, a new day). Reuses
/// the F13 availability query so only genuinely free, server-priced slots are
/// offered; the backend re-checks overlap and re-prices on submit. Returns the
/// chosen slot id (the caller posts the reschedule), or null when dismissed.
Future<int?> showRescheduleModal(
  BuildContext context, {
  required int courtId,
  required int currentSlotId,
  required String reference,
}) {
  return showDialog<int>(
    context: context,
    builder: (_) => _RescheduleModal(
      courtId: courtId,
      currentSlotId: currentSlotId,
      reference: reference,
    ),
  );
}

class _RescheduleModal extends ConsumerStatefulWidget {
  const _RescheduleModal({
    required this.courtId,
    required this.currentSlotId,
    required this.reference,
  });

  final int courtId;
  final int currentSlotId;
  final String reference;

  @override
  ConsumerState<_RescheduleModal> createState() => _RescheduleModalState();
}

class _RescheduleModalState extends ConsumerState<_RescheduleModal> {
  late DateTime _date; // date-only (keeps the availability family key stable)
  int? _selectedSlotId;

  @override
  void initState() {
    super.initState();
    final tomorrow = DateTime.now().add(const Duration(days: 1));
    _date = DateTime(tomorrow.year, tomorrow.month, tomorrow.day);
  }

  void _pickDate(DateTime value) {
    setState(() {
      _date = DateTime(value.year, value.month, value.day);
      _selectedSlotId = null; // a new day invalidates the previous pick
    });
  }

  @override
  Widget build(BuildContext context) {
    final availability =
        ref.watch(slotAvailabilityProvider((courtId: widget.courtId, date: _date)));

    return FormScaffold(
      title: 'Reschedule ${widget.reference}',
      subtitle: 'Pick a new free slot for this court. The price updates to the '
          'new slot and availability is re-checked on the server.',
      onClose: () => Navigator.of(context).pop(),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: const Text('Cancel'),
        ),
        ElevatedButton(
          onPressed: _selectedSlotId == null
              ? null
              : () => Navigator.of(context).pop(_selectedSlotId),
          child: const Text('Reschedule'),
        ),
      ],
      children: [
        DateTimePickerField(
          label: 'New date',
          value: _date,
          includeTime: false,
          firstDate: DateTime.now(),
          onChanged: _pickDate,
        ),
        const SizedBox(height: AppSpacing.sm),
        SizedBox(
          height: 220,
          child: AsyncValueView<DayAvailability>(
            value: availability,
            onRetry: () => ref.invalidate(
                slotAvailabilityProvider((courtId: widget.courtId, date: _date))),
            data: (day) => FreeSlotPicker(
              day: day,
              excludeSlotId: widget.currentSlotId,
              selectedSlotId: _selectedSlotId,
              onSelected: (id) => setState(() => _selectedSlotId = id),
            ),
          ),
        ),
      ],
    );
  }
}
