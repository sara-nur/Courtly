import 'package:flutter/material.dart';

import '../theme/app_colors.dart';
import '../theme/app_spacing.dart';
import '../utils/formatters.dart';

/// A form field whose date/time value is chosen exclusively through
/// platform pickers and never typed by hand.
///
/// Renders as a tappable [InputDecorator] that opens a date picker (and,
/// when [includeTime] is set, a time picker) on tap. Validation errors are
/// surfaced below the field via the field's [InputDecoration.errorText].
class DateTimePickerField extends StatelessWidget {
  /// Creates a [DateTimePickerField].
  const DateTimePickerField({
    super.key,
    this.value,
    required this.onChanged,
    this.label,
    this.firstDate,
    this.lastDate,
    this.enabled = true,
    this.includeTime = true,
    this.validator,
  });

  /// Currently selected date/time, or null when nothing is chosen yet.
  final DateTime? value;

  /// Called with the newly chosen date/time once a selection completes.
  final ValueChanged<DateTime> onChanged;

  /// Optional label rendered by the input decoration.
  final String? label;

  /// Earliest selectable date. Defaults to the year 2000.
  final DateTime? firstDate;

  /// Latest selectable date. Defaults to the year 2100.
  final DateTime? lastDate;

  /// Whether the field responds to taps.
  final bool enabled;

  /// Whether a time picker follows the date picker.
  final bool includeTime;

  /// Optional validator returning an error message, or null when valid.
  final String? Function(DateTime?)? validator;

  Future<void> _pick(BuildContext context, FormFieldState<DateTime> field) async {
    final DateTime? pickedDate = await showDatePicker(
      context: context,
      initialDate: value ?? DateTime.now(),
      firstDate: firstDate ?? DateTime(2000),
      lastDate: lastDate ?? DateTime(2100),
    );
    if (pickedDate == null) {
      return;
    }

    DateTime result = pickedDate;
    if (includeTime) {
      if (!context.mounted) {
        return;
      }
      final TimeOfDay? pickedTime = await showTimePicker(
        context: context,
        initialTime: TimeOfDay.fromDateTime(value ?? DateTime.now()),
        // Open in keyboard-entry mode (type HH:MM) — friendlier than the clock dial,
        // especially on desktop; the user can still toggle to the dial.
        initialEntryMode: TimePickerEntryMode.input,
      );
      if (pickedTime == null) {
        return;
      }
      result = DateTime(
        pickedDate.year,
        pickedDate.month,
        pickedDate.day,
        pickedTime.hour,
        pickedTime.minute,
      );
    }

    field.didChange(result);
    onChanged(result);
  }

  @override
  Widget build(BuildContext context) {
    return FormField<DateTime>(
      initialValue: value,
      validator: validator,
      builder: (FormFieldState<DateTime> field) {
        final DateTime? current = field.value;
        return InkWell(
          borderRadius: AppSpacing.brMd,
          onTap: enabled ? () => _pick(context, field) : null,
          child: InputDecorator(
            decoration: InputDecoration(
              labelText: label,
              enabled: enabled,
              suffixIcon: const Icon(Icons.event),
              errorText: field.errorText,
            ),
            isEmpty: current == null,
            child: current != null
                ? Text(
                    includeTime
                        ? Formatters.dateTime(current)
                        : Formatters.date(current),
                  )
                : Text(
                    includeTime ? 'Select date and time' : 'Select date',
                    style: TextStyle(color: AppColors.textMuted),
                  ),
          ),
        );
      },
    );
  }
}
