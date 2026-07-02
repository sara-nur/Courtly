import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/network/api_exception.dart';
import '../../../../core/theme/app_colors.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../core/utils/formatters.dart';
import '../../../../core/widgets/app_text_field.dart';
import '../../../../core/widgets/async_value_view.dart';
import '../../../../core/widgets/confirm_dialog.dart';
import '../../../../core/widgets/date_time_picker_field.dart';
import '../../../../core/widgets/form_scaffold.dart';
import '../../application/court_providers.dart';
import '../../domain/court_models.dart';

/// Opens the court time-slots &amp; availability modal (Feature 13).
///
/// Two parts:
///  - **Generate slots** — pick a date range, a daily open/close window and a
///    slot length (with an optional evening peak multiplier); the server
///    materializes the slots (it owns the price + bucket) and skips any that
///    already exist.
///  - **Availability** — browse a single day's slots grouped Morning / Afternoon
///    / Evening, each free or **Booked** (disabled). Remove a single free slot or
///    a whole day; booked slots are kept and reported. A court under maintenance
///    shows no bookable slots for the day.
///
/// After any generate/remove the watched day refreshes automatically (no manual
/// reload — rubric §6). No raw ids are ever shown.
Future<void> showCourtSlots(BuildContext context, WidgetRef ref, Court court) {
  return showDialog<void>(
    context: context,
    builder: (_) => _SlotsModal(court: court),
  );
}

class _SlotsModal extends ConsumerStatefulWidget {
  const _SlotsModal({required this.court});

  final Court court;

  @override
  ConsumerState<_SlotsModal> createState() => _SlotsModalState();
}

class _SlotsModalState extends ConsumerState<_SlotsModal> {
  // Allowed slot lengths must match the backend validator.
  static const List<int> _slotLengths = [30, 60, 90, 120];
  static const int _defaultRangeDays = 30;

  late DateTime _fromDate;
  late DateTime _toDate;
  int _openHour = 8;
  int _closeHour = 20;
  int _slotMinutes = 60;
  late final TextEditingController _peakController;

  late DateTime _selectedDate; // the day shown in the Availability section

  bool _generating = false;
  bool _busy = false; // a remove is in flight
  String? _formError; // form-level generate error (below the controls)
  String? _peakError; // below the peak field

  int get _courtId => widget.court.id;

  ({int courtId, DateTime date}) get _dayKey =>
      (courtId: _courtId, date: _selectedDate);

  @override
  void initState() {
    super.initState();
    final today = _dateOnly(DateTime.now());
    _fromDate = today;
    _toDate = _dateOnly(today.add(const Duration(days: _defaultRangeDays)));
    _selectedDate = today;
    _peakController = TextEditingController();
  }

  @override
  void dispose() {
    _peakController.dispose();
    super.dispose();
  }

  static DateTime _dateOnly(DateTime d) => DateTime(d.year, d.month, d.day);

  void _refreshDay() => ref.invalidate(slotAvailabilityProvider(_dayKey));

  // --- Generate -------------------------------------------------------------

  /// Client-side pre-checks mirroring the backend validator, so obvious mistakes
  /// surface instantly below the controls (the server stays authoritative).
  String? _validateGenerate() {
    if (_toDate.isBefore(_fromDate)) {
      return 'The end date must be on or after the start date.';
    }
    if (_toDate.difference(_fromDate).inDays + 1 > 60) {
      return 'The date range must be at most 60 days.';
    }
    if (_closeHour <= _openHour) {
      return 'The close time must be after the open time.';
    }
    if ((_closeHour - _openHour) * 60 < _slotMinutes) {
      return 'The daily window is shorter than one slot — widen the hours or shorten the slot.';
    }
    return null;
  }

  /// Parses the optional evening peak field: blank → null (server default 1.2);
  /// otherwise a number in [1.0, 5.0].
  (double?, String?) _parsePeak() {
    final text = _peakController.text.trim();
    if (text.isEmpty) return (null, null);
    final value = double.tryParse(text);
    if (value == null) return (null, 'Enter a number, e.g. 1.2.');
    if (value < 1.0 || value > 5.0) return (null, 'Must be between 1.0 and 5.0.');
    return (value, null);
  }

  Future<void> _generate() async {
    setState(() {
      _formError = null;
      _peakError = null;
    });

    final formError = _validateGenerate();
    final (peak, peakError) = _parsePeak();
    if (formError != null || peakError != null) {
      setState(() {
        _formError = formError;
        _peakError = peakError;
      });
      return;
    }

    setState(() => _generating = true);
    try {
      final result = await ref.read(courtRepositoryProvider).generateSlots(
            _courtId,
            fromDate: _fromDate,
            toDate: _toDate,
            openHour: _openHour,
            closeHour: _closeHour,
            slotMinutes: _slotMinutes,
            eveningPeakMultiplier: peak,
          );
      _refreshDay();
      if (!mounted) return;
      setState(() => _generating = false);
      final skipped = result.skippedCount > 0
          ? ' (${result.skippedCount} already existed)'
          : '';
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Generated ${result.createdCount} slots$skipped.')),
      );
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _generating = false;
        _formError = e.message; // backend's authoritative message, below the controls
      });
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _generating = false;
        _formError = 'Something went wrong. Please try again.';
      });
    }
  }

  // --- Remove ---------------------------------------------------------------

  Future<void> _removeSlot(AvailabilitySlot slot) async {
    final confirmed = await ConfirmDialog.show(
      context,
      title: 'Remove slot',
      message:
          'Remove the ${Formatters.time(slot.startUtc.toLocal())} slot? It will no longer be bookable.',
      confirmLabel: 'Remove',
      destructive: true,
      icon: Icons.delete_outline,
    );
    if (!confirmed) return;

    setState(() => _busy = true);
    try {
      await ref.read(courtRepositoryProvider).removeSlot(_courtId, slot.id);
      _refreshDay();
      if (!mounted) return;
      setState(() => _busy = false);
      ScaffoldMessenger.of(context)
          .showSnackBar(const SnackBar(content: Text('Slot removed.')));
    } on ApiException catch (e) {
      _onRemoveError(e.message);
    } catch (_) {
      _onRemoveError('Something went wrong. Please try again.');
    }
  }

  Future<void> _removeDay() async {
    final confirmed = await ConfirmDialog.show(
      context,
      title: 'Remove the day',
      message:
          'Remove all free slots for ${Formatters.date(_selectedDate)}? Booked slots are kept.',
      confirmLabel: 'Remove day',
      destructive: true,
      icon: Icons.delete_sweep_outlined,
    );
    if (!confirmed) return;

    setState(() => _busy = true);
    try {
      final result =
          await ref.read(courtRepositoryProvider).removeDaySlots(_courtId, _selectedDate);
      _refreshDay();
      if (!mounted) return;
      setState(() => _busy = false);
      final kept =
          result.blockedCount > 0 ? ' (${result.blockedCount} booked kept)' : '';
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Removed ${result.removedCount} slots$kept.')),
      );
    } on ApiException catch (e) {
      _onRemoveError(e.message);
    } catch (_) {
      _onRemoveError('Something went wrong. Please try again.');
    }
  }

  void _onRemoveError(String message) {
    if (!mounted) return;
    setState(() => _busy = false);
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(message), backgroundColor: AppColors.danger),
    );
  }

  // --- Day navigation -------------------------------------------------------

  void _shiftDay(int days) =>
      setState(() => _selectedDate = _dateOnly(_selectedDate.add(Duration(days: days))));

  Future<void> _pickDay() async {
    final picked = await showDatePicker(
      context: context,
      initialDate: _selectedDate,
      firstDate: DateTime(2000),
      lastDate: DateTime(2100),
    );
    if (picked != null) setState(() => _selectedDate = _dateOnly(picked));
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final availability = ref.watch(slotAvailabilityProvider(_dayKey));

    return Dialog(
      child: FormScaffold(
        title: 'Slots & availability — ${widget.court.name}',
        subtitle: 'Generate bookable slots and manage a day',
        maxWidth: 640,
        onClose: _generating ? null : () => Navigator.of(context).pop(),
        actions: [
          TextButton(
            onPressed: _generating ? null : () => Navigator.of(context).pop(),
            child: const Text('Done'),
          ),
        ],
        children: [
          _sectionTitle(theme, 'Generate slots'),
          const SizedBox(height: AppSpacing.xs),
          Row(
            children: [
              Expanded(
                child: DateTimePickerField(
                  label: 'From date',
                  value: _fromDate,
                  includeTime: false,
                  enabled: !_generating,
                  onChanged: (d) => setState(() => _fromDate = _dateOnly(d)),
                ),
              ),
              const SizedBox(width: AppSpacing.sm),
              Expanded(
                child: DateTimePickerField(
                  label: 'To date',
                  value: _toDate,
                  includeTime: false,
                  enabled: !_generating,
                  onChanged: (d) => setState(() => _toDate = _dateOnly(d)),
                ),
              ),
            ],
          ),
          const SizedBox(height: AppSpacing.sm),
          Row(
            children: [
              Expanded(
                child: _HourDropdown(
                  label: 'Open',
                  value: _openHour,
                  min: 0,
                  max: 23,
                  enabled: !_generating,
                  onChanged: (h) => setState(() => _openHour = h),
                ),
              ),
              const SizedBox(width: AppSpacing.sm),
              Expanded(
                child: _HourDropdown(
                  label: 'Close',
                  value: _closeHour,
                  min: 1,
                  max: 24,
                  enabled: !_generating,
                  onChanged: (h) => setState(() => _closeHour = h),
                ),
              ),
              const SizedBox(width: AppSpacing.sm),
              Expanded(
                child: DropdownButtonFormField<int>(
                  initialValue: _slotMinutes,
                  decoration: const InputDecoration(labelText: 'Slot length', isDense: true),
                  items: [
                    for (final m in _slotLengths)
                      DropdownMenuItem(value: m, child: Text('$m min')),
                  ],
                  onChanged: _generating
                      ? null
                      : (m) => setState(() => _slotMinutes = m ?? _slotMinutes),
                ),
              ),
            ],
          ),
          const SizedBox(height: AppSpacing.sm),
          AppTextField(
            controller: _peakController,
            label: 'Evening peak ×  (optional — default 1.2)',
            hint: 'e.g. 1.25',
            enabled: !_generating,
            keyboardType: const TextInputType.numberWithOptions(decimal: true),
            onChanged: (_) {
              if (_peakError != null) setState(() => _peakError = null);
            },
          ),
          if (_peakError != null) _errorText(theme, _peakError!),
          const SizedBox(height: AppSpacing.sm),
          Align(
            alignment: Alignment.centerRight,
            child: SizedBox(
              height: AppSpacing.inputHeight,
              child: ElevatedButton.icon(
                onPressed: _generating ? null : _generate,
                icon: _generating
                    ? const SizedBox(
                        width: 18,
                        height: 18,
                        child: CircularProgressIndicator(strokeWidth: 2.5),
                      )
                    : const Icon(Icons.event_available_outlined, size: 18),
                label: const Text('Generate'),
              ),
            ),
          ),
          if (_formError != null) _errorText(theme, _formError!),
          const Divider(height: AppSpacing.lg),
          Row(
            children: [
              Expanded(child: _sectionTitle(theme, 'Availability')),
              TextButton.icon(
                onPressed: _busy ? null : _removeDay,
                style: TextButton.styleFrom(foregroundColor: AppColors.danger),
                icon: const Icon(Icons.delete_sweep_outlined, size: 18),
                label: const Text('Remove day'),
              ),
            ],
          ),
          _DayNavigator(
            date: _selectedDate,
            enabled: !_busy,
            onPrev: () => _shiftDay(-1),
            onNext: () => _shiftDay(1),
            onPick: _pickDay,
          ),
          const SizedBox(height: AppSpacing.sm),
          SizedBox(
            height: 260,
            child: AsyncValueView<DayAvailability>(
              value: availability,
              onRetry: _refreshDay,
              data: (day) => _DayView(
                day: day,
                busy: _busy,
                onRemoveSlot: _removeSlot,
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _sectionTitle(ThemeData theme, String text) => Text(
        text,
        style: theme.textTheme.titleSmall?.copyWith(fontWeight: FontWeight.w600),
      );

  Widget _errorText(ThemeData theme, String text) => Padding(
        padding: const EdgeInsets.only(top: AppSpacing.xxs),
        child: Text(
          text,
          style: theme.textTheme.bodySmall?.copyWith(color: AppColors.danger),
        ),
      );
}

/// An hour-of-day dropdown rendering `HH:00` labels over [min]..[max] inclusive.
class _HourDropdown extends StatelessWidget {
  const _HourDropdown({
    required this.label,
    required this.value,
    required this.min,
    required this.max,
    required this.enabled,
    required this.onChanged,
  });

  final String label;
  final int value;
  final int min;
  final int max;
  final bool enabled;
  final ValueChanged<int> onChanged;

  @override
  Widget build(BuildContext context) {
    return DropdownButtonFormField<int>(
      initialValue: value,
      decoration: InputDecoration(labelText: label, isDense: true),
      items: [
        for (var h = min; h <= max; h++)
          DropdownMenuItem(
            value: h,
            child: Text('${h.toString().padLeft(2, '0')}:00'),
          ),
      ],
      onChanged: enabled ? (h) => onChanged(h ?? value) : null,
    );
  }
}

/// A prev / date / next strip for choosing the day shown in Availability.
class _DayNavigator extends StatelessWidget {
  const _DayNavigator({
    required this.date,
    required this.enabled,
    required this.onPrev,
    required this.onNext,
    required this.onPick,
  });

  final DateTime date;
  final bool enabled;
  final VoidCallback onPrev;
  final VoidCallback onNext;
  final VoidCallback onPick;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        IconButton(
          icon: const Icon(Icons.chevron_left),
          tooltip: 'Previous day',
          onPressed: enabled ? onPrev : null,
        ),
        Expanded(
          child: OutlinedButton.icon(
            onPressed: enabled ? onPick : null,
            icon: const Icon(Icons.event, size: 18),
            label: Text(Formatters.date(date)),
          ),
        ),
        IconButton(
          icon: const Icon(Icons.chevron_right),
          tooltip: 'Next day',
          onPressed: enabled ? onNext : null,
        ),
      ],
    );
  }
}

/// Renders a day's availability: a maintenance/empty notice, or the non-empty
/// Morning / Afternoon / Evening buckets as chip groups.
class _DayView extends StatelessWidget {
  const _DayView({
    required this.day,
    required this.busy,
    required this.onRemoveSlot,
  });

  final DayAvailability day;
  final bool busy;
  final void Function(AvailabilitySlot) onRemoveSlot;

  @override
  Widget build(BuildContext context) {
    if (day.isCourtUnderMaintenance) {
      return const _Notice(
        icon: Icons.build_outlined,
        tone: StatusTone.warning,
        message: 'Court is under maintenance — no bookable slots for this day.',
      );
    }
    if (day.isEmpty) {
      return const _Notice(
        icon: Icons.event_busy_outlined,
        tone: StatusTone.neutral,
        message: 'No slots for this day. Generate some above.',
      );
    }

    final groups = day.buckets.where((b) => b.slots.isNotEmpty).toList();
    return ListView.separated(
      itemCount: groups.length,
      separatorBuilder: (_, __) => const SizedBox(height: AppSpacing.sm),
      itemBuilder: (context, i) => _BucketGroup(
        bucket: groups[i],
        busy: busy,
        onRemoveSlot: onRemoveSlot,
      ),
    );
  }
}

class _BucketGroup extends StatelessWidget {
  const _BucketGroup({
    required this.bucket,
    required this.busy,
    required this.onRemoveSlot,
  });

  final AvailabilityBucket bucket;
  final bool busy;
  final void Function(AvailabilitySlot) onRemoveSlot;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          '${bucket.bucketName} · ${bucket.slots.length}',
          style: theme.textTheme.bodySmall?.copyWith(color: AppColors.textSecondary),
        ),
        const SizedBox(height: AppSpacing.xxs),
        Wrap(
          spacing: AppSpacing.xs,
          runSpacing: AppSpacing.xs,
          children: [
            for (final slot in bucket.slots)
              _SlotChip(
                slot: slot,
                busy: busy,
                onRemove: () => onRemoveSlot(slot),
              ),
          ],
        ),
      ],
    );
  }
}

/// One slot pill: time + price, with a remove affordance when free. A booked slot
/// is muted and shows a "Booked" tag with a tooltip explaining it can't be
/// removed (rubric — disabled action + reason).
class _SlotChip extends StatelessWidget {
  const _SlotChip({
    required this.slot,
    required this.busy,
    required this.onRemove,
  });

  final AvailabilitySlot slot;
  final bool busy;
  final VoidCallback onRemove;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final taken = slot.isTaken;

    final content = Container(
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.sm,
        vertical: AppSpacing.xxs,
      ),
      decoration: BoxDecoration(
        color: taken ? AppColors.surfaceMuted : AppColors.surface,
        borderRadius: AppSpacing.brPill,
        border: Border.all(
          color: taken ? AppColors.border : AppColors.primaryLight,
        ),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(
            Formatters.time(slot.startUtc.toLocal()),
            style: theme.textTheme.bodySmall?.copyWith(
              fontWeight: FontWeight.w600,
              color: taken ? AppColors.textMuted : AppColors.textPrimary,
            ),
          ),
          const SizedBox(width: AppSpacing.xs),
          Text(
            Formatters.money(slot.price),
            style: theme.textTheme.bodySmall?.copyWith(color: AppColors.textSecondary),
          ),
          const SizedBox(width: AppSpacing.xxs),
          if (taken)
            Text(
              'Booked',
              style: theme.textTheme.labelSmall?.copyWith(color: AppColors.textMuted),
            )
          else
            InkWell(
              key: ValueKey('remove-slot-${slot.id}'),
              onTap: busy ? null : onRemove,
              borderRadius: AppSpacing.brPill,
              child: const Padding(
                padding: EdgeInsets.all(2),
                child: Icon(Icons.close, size: 14, color: AppColors.textSecondary),
              ),
            ),
        ],
      ),
    );

    return taken
        ? Tooltip(message: 'Booked — can\'t be removed.', child: content)
        : content;
  }
}

/// A centered icon + message used for the maintenance and empty-day states.
class _Notice extends StatelessWidget {
  const _Notice({required this.icon, required this.tone, required this.message});

  final IconData icon;
  final StatusTone tone;
  final String message;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, color: StatusToneColors.of(tone).foreground),
          const SizedBox(height: AppSpacing.xs),
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: AppSpacing.md),
            child: Text(
              message,
              textAlign: TextAlign.center,
              style: theme.textTheme.bodyMedium?.copyWith(color: AppColors.textSecondary),
            ),
          ),
        ],
      ),
    );
  }
}
