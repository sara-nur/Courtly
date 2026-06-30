import 'package:flutter/material.dart';

import '../../../../core/theme/app_colors.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../domain/dashboard_models.dart';

/// The "Business Health Check" banner (PRD p.5). Renders the server-derived
/// health status as a tinted strip — green (healthy), amber (watch) or red
/// (at risk) — with a headline and a short explanation.
class HealthBanner extends StatelessWidget {
  const HealthBanner({super.key, required this.health});

  final HealthCheck health;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final tone = _toneFor(health.status);
    final colors = StatusToneColors.of(tone);

    return Container(
      width: double.infinity,
      padding: AppSpacing.cardPadding,
      decoration: BoxDecoration(
        color: colors.background,
        borderRadius: AppSpacing.brMd,
        border: Border.all(color: colors.foreground),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(_iconFor(health.status), color: colors.foreground),
          const SizedBox(width: AppSpacing.sm),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  health.headline,
                  style: theme.textTheme.titleSmall?.copyWith(color: colors.foreground),
                ),
                const SizedBox(height: AppSpacing.xxs),
                Text(
                  health.detail,
                  style: theme.textTheme.bodyMedium?.copyWith(color: AppColors.textSecondary),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  static StatusTone _toneFor(DashboardHealthStatus status) => switch (status) {
        DashboardHealthStatus.healthy => StatusTone.success,
        DashboardHealthStatus.watch => StatusTone.warning,
        DashboardHealthStatus.atRisk => StatusTone.danger,
      };

  static IconData _iconFor(DashboardHealthStatus status) => switch (status) {
        DashboardHealthStatus.healthy => Icons.check_circle_outline,
        DashboardHealthStatus.watch => Icons.info_outline,
        DashboardHealthStatus.atRisk => Icons.warning_amber_outlined,
      };
}
