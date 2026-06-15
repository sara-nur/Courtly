import 'package:flutter/material.dart';

/// A generic dropdown form field backed by a reference list of [T] values.
///
/// Renders each option using [itemLabel] so that human-readable text is shown
/// instead of raw identifiers. When [items] is empty the field is rendered
/// disabled with a hint of `No options available`, making dependent forms
/// visibly blocked when the underlying reference table has no rows.
class DbDropdown<T> extends StatelessWidget {
  /// Currently selected value, or `null` when nothing is selected.
  final T? value;

  /// The available options to choose from.
  final List<T> items;

  /// Maps an item to its user-facing label. Must never expose raw IDs.
  final String Function(T) itemLabel;

  /// Called when the user selects a different value.
  final ValueChanged<T?>? onChanged;

  /// Optional label shown above/inside the field via the input decoration.
  final String? label;

  /// Optional placeholder shown when no value is selected.
  final String? hint;

  /// Optional validator; errors render below the field via the theme.
  final String? Function(T?)? validator;

  /// Whether the field is interactive. Ignored when [items] is empty.
  final bool enabled;

  const DbDropdown({
    super.key,
    this.value,
    required this.items,
    required this.itemLabel,
    this.onChanged,
    this.label,
    this.hint,
    this.validator,
    this.enabled = true,
  });

  @override
  Widget build(BuildContext context) {
    final bool hasOptions = items.isNotEmpty;
    final bool isEnabled = enabled && hasOptions;

    return DropdownButtonFormField<T>(
      value: value,
      items: [
        for (final item in items)
          DropdownMenuItem<T>(
            value: item,
            child: Text(itemLabel(item)),
          ),
      ],
      onChanged: isEnabled ? onChanged : null,
      validator: validator,
      decoration: InputDecoration(
        labelText: label,
        hintText: hasOptions ? hint : 'No options available',
      ),
    );
  }
}
