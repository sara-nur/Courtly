import 'package:flutter/material.dart';

/// Wraps an action and, when unavailable, both disables it and explains why.
///
/// When [enabled] is `true`, the [child] is returned unchanged. When disabled,
/// the action stays visibly present but is greyed-out and non-interactive,
/// with the [reason] surfaced on hover or long-press via a [Tooltip].
class DisabledAction extends StatelessWidget {
  const DisabledAction({
    super.key,
    required this.enabled,
    required this.child,
    this.reason,
  });

  /// Whether the wrapped action is currently available.
  final bool enabled;

  /// The action to wrap (e.g. a button).
  final Widget child;

  /// Optional explanation shown when the action is disabled.
  final String? reason;

  @override
  Widget build(BuildContext context) {
    if (enabled) {
      return child;
    }

    return Tooltip(
      message: reason ?? 'This action is currently unavailable',
      child: Opacity(
        opacity: 0.5,
        child: IgnorePointer(
          child: child,
        ),
      ),
    );
  }
}
