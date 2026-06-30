import 'package:flutter/material.dart';

import '../../../../core/theme/app_colors.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../domain/dashboard_models.dart';

/// One KPI card (PRD p.5): an icon + label, the formatted value, and a delta
/// chip comparing the selected window to the prior equal-length period. Higher
/// is better for every dashboard KPI, so an increase is green and a decrease is
/// red; a growth-from-zero (no comparable prior value) shows a neutral "New".
class KpiCard extends StatelessWidget {
  const KpiCard({
    super.key,
    required this.label,
    required this.value,
    required this.icon,
    required this.kpi,
  });

  final String label;
  final String value;
  final IconData icon;
  final Kpi kpi;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Card(
      child: Padding(
        padding: AppSpacing.cardPadding,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            Row(
              children: [
                Icon(icon, size: 18, color: AppColors.textMuted),
                const SizedBox(width: AppSpacing.xs),
                Expanded(
                  child: Text(
                    label,
                    style: theme.textTheme.bodyMedium?.copyWith(color: AppColors.textSecondary),
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
              ],
            ),
            const SizedBox(height: AppSpacing.sm),
            Text(value, style: theme.textTheme.headlineSmall),
            const SizedBox(height: AppSpacing.xs),
            _DeltaChip(kpi: kpi),
          ],
        ),
      ),
    );
  }
}

class _DeltaChip extends StatelessWidget {
  const _DeltaChip({required this.kpi});

  final Kpi kpi;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    if (!kpi.hasDelta) {
      return Text(
        kpi.current > 0 ? 'New' : 'No change vs prior period',
        style: theme.textTheme.bodySmall?.copyWith(color: AppColors.textMuted),
      );
    }

    final up = kpi.isUp;
    final color = up ? AppColors.success : AppColors.danger;
    final delta = kpi.deltaPercent!.abs();

    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(up ? Icons.arrow_upward : Icons.arrow_downward, size: 14, color: color),
        const SizedBox(width: AppSpacing.xxs),
        Text(
          '${delta.toStringAsFixed(1)}%',
          style: theme.textTheme.bodySmall?.copyWith(color: color, fontWeight: FontWeight.w600),
        ),
        const SizedBox(width: AppSpacing.xxs),
        Flexible(
          child: Text(
            'vs prior period',
            style: theme.textTheme.bodySmall?.copyWith(color: AppColors.textMuted),
            overflow: TextOverflow.ellipsis,
          ),
        ),
      ],
    );
  }
}
