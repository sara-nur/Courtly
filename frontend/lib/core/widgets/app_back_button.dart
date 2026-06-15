import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

/// Mandatory Back control used to navigate to the previous route.
///
/// Defaults to popping the current route when [onPressed] is not provided,
/// guarding against an empty navigation stack via [GoRouter.canPop].
class AppBackButton extends StatelessWidget {
  const AppBackButton({
    super.key,
    this.onPressed,
    this.label = 'Back',
  });

  final VoidCallback? onPressed;
  final String label;

  @override
  Widget build(BuildContext context) {
    return TextButton.icon(
      icon: const Icon(Icons.arrow_back, size: 18),
      label: Text(label),
      onPressed: onPressed ??
          () {
            if (context.canPop()) {
              context.pop();
            }
          },
    );
  }
}
