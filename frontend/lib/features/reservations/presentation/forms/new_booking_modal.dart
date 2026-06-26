import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/network/api_exception.dart';
import '../../../../core/theme/app_colors.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../core/widgets/async_value_view.dart';
import '../../../../core/widgets/date_time_picker_field.dart';
import '../../../../core/widgets/db_dropdown.dart';
import '../../../../core/widgets/form_scaffold.dart';
import '../../../court_catalog/application/court_providers.dart';
import '../../../court_catalog/domain/court_models.dart';
import '../../../users/application/user_providers.dart';
import '../../../users/domain/user_summary.dart';
import '../../application/reservation_providers.dart';
import '../../domain/reservation_models.dart';
import '../widgets/free_slot_picker.dart';

/// Admin "+ New Booking": book a court for a customer (rubric §5 allows admins to
/// act on other users' data). Pick a customer + court + day, then a free
/// server-priced slot (reusing the F13 availability query); the server owns the
/// price, validates the customer, and re-checks overlap. Returns the created
/// [ReservationDetail] on success (the caller refreshes the list + opens it), or
/// null when dismissed. Customer/court dropdowns are DB-fed (never free-text).
Future<ReservationDetail?> showNewBookingModal(BuildContext context) {
  return showDialog<ReservationDetail>(
    context: context,
    builder: (_) => const _NewBookingModal(),
  );
}

class _NewBookingModal extends ConsumerStatefulWidget {
  const _NewBookingModal();

  @override
  ConsumerState<_NewBookingModal> createState() => _NewBookingModalState();
}

class _NewBookingModalState extends ConsumerState<_NewBookingModal> {
  UserSummary? _customer;
  Court? _court;
  late DateTime _date; // date-only
  int? _slotId;
  bool _submitting = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    final tomorrow = DateTime.now().add(const Duration(days: 1));
    _date = DateTime(tomorrow.year, tomorrow.month, tomorrow.day);
  }

  bool get _canSubmit =>
      _customer != null && _court != null && _slotId != null && !_submitting;

  Future<void> _submit() async {
    if (!_canSubmit) return;
    setState(() {
      _submitting = true;
      _error = null;
    });
    try {
      final detail = await ref.read(reservationRepositoryProvider).createForUser(
            timeSlotId: _slotId!,
            userId: _customer!.id,
          );
      if (mounted) Navigator.of(context).pop(detail);
    } on ApiException catch (e) {
      if (mounted) {
        setState(() {
          _submitting = false;
          _error = e.message;
        });
      }
    } catch (_) {
      if (mounted) {
        setState(() {
          _submitting = false;
          _error = 'Something went wrong. Please try again.';
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final customers = ref.watch(userLookupProvider);
    final courts = ref.watch(reservationCourtLookupProvider);
    final court = _court;

    return FormScaffold(
      title: 'New Booking',
      subtitle: 'Create a reservation on behalf of a customer. The server owns '
          'the price and re-checks availability.',
      onClose: _submitting ? null : () => Navigator.of(context).pop(),
      actions: [
        TextButton(
          onPressed: _submitting ? null : () => Navigator.of(context).pop(),
          child: const Text('Cancel'),
        ),
        ElevatedButton(
          onPressed: _canSubmit ? _submit : null,
          child: _submitting
              ? const SizedBox(
                  width: 18,
                  height: 18,
                  child: CircularProgressIndicator(strokeWidth: 2),
                )
              : const Text('Create booking'),
        ),
      ],
      children: [
        DbDropdown<UserSummary>(
          label: 'Customer',
          hint: 'Select a customer',
          value: _customer,
          items: customers.maybeWhen(data: (list) => list, orElse: () => const []),
          itemLabel: (u) => u.pickerLabel,
          onChanged:
              _submitting ? null : (u) => setState(() => _customer = u),
        ),
        DbDropdown<Court>(
          label: 'Court',
          hint: 'Select a court',
          value: _court,
          items: courts.maybeWhen(data: (list) => list, orElse: () => const []),
          itemLabel: (c) => c.name,
          onChanged: _submitting
              ? null
              : (c) => setState(() {
                    _court = c;
                    _slotId = null; // a new court invalidates the slot pick
                  }),
        ),
        DateTimePickerField(
          label: 'Date',
          value: _date,
          includeTime: false,
          firstDate: DateTime.now(),
          enabled: !_submitting,
          onChanged: (value) => setState(() {
            _date = DateTime(value.year, value.month, value.day);
            _slotId = null;
          }),
        ),
        const SizedBox(height: AppSpacing.xs),
        SizedBox(
          height: 200,
          child: court == null
              ? const _PickCourtFirst()
              : AsyncValueView<DayAvailability>(
                  value: ref.watch(slotAvailabilityProvider(
                      (courtId: court.id, date: _date))),
                  onRetry: () => ref.invalidate(slotAvailabilityProvider(
                      (courtId: court.id, date: _date))),
                  data: (day) => FreeSlotPicker(
                    day: day,
                    selectedSlotId: _slotId,
                    onSelected: (id) => setState(() => _slotId = id),
                  ),
                ),
        ),
        if (_error != null) ...[
          const SizedBox(height: AppSpacing.sm),
          Text(_error!, style: const TextStyle(color: AppColors.danger)),
        ],
      ],
    );
  }
}

class _PickCourtFirst extends StatelessWidget {
  const _PickCourtFirst();

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Text(
        'Pick a court to see available slots.',
        style: Theme.of(context)
            .textTheme
            .bodyMedium
            ?.copyWith(color: AppColors.textSecondary),
      ),
    );
  }
}
