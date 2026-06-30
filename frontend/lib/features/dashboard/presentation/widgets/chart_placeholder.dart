import 'package:flutter/material.dart';

import '../../../../core/theme/app_colors.dart';

/// Centered muted message shown inside a chart card when there is nothing to
/// plot for the selected window. Shared by the dashboard charts (DRY).
class ChartPlaceholder extends StatelessWidget {
  const ChartPlaceholder({super.key, required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Text(
        message,
        style: Theme.of(context).textTheme.bodyMedium?.copyWith(color: AppColors.textMuted),
      ),
    );
  }
}
