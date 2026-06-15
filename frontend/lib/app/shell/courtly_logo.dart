import 'package:flutter/material.dart';

import '../../core/theme/app_colors.dart';
import '../../core/theme/app_spacing.dart';

/// The Courtly wordmark used in both shells. The admin build appends a blue
/// "Admin" suffix, matching `ui_design_and_scope.pdf` ("CourtlyAdmin").
class CourtlyLogo extends StatelessWidget {
  const CourtlyLogo({super.key, this.showAdminSuffix = false});

  final bool showAdminSuffix;

  @override
  Widget build(BuildContext context) {
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Container(
          padding: const EdgeInsets.all(AppSpacing.xxs),
          decoration: BoxDecoration(
            color: AppColors.primary,
            borderRadius: AppSpacing.brSm,
          ),
          child: const Icon(
            Icons.sports_tennis,
            size: 18,
            color: AppColors.onPrimary,
          ),
        ),
        const SizedBox(width: AppSpacing.xs),
        RichText(
          text: TextSpan(
            style: const TextStyle(
              fontSize: 18,
              fontWeight: FontWeight.w800,
              color: AppColors.textPrimary,
            ),
            children: [
              const TextSpan(text: 'Courtly'),
              if (showAdminSuffix)
                const TextSpan(
                  text: 'Admin',
                  style: TextStyle(color: AppColors.primary),
                ),
            ],
          ),
        ),
      ],
    );
  }
}
