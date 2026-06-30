import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';

import '../../../../core/theme/app_colors.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../core/utils/formatters.dart';
import '../../domain/dashboard_models.dart';
import 'chart_placeholder.dart';
import 'dashboard_card.dart';

/// Revenue Trends line chart (PRD p.5): net daily revenue across the selected
/// window. Built on `fl_chart`; x is the day index, y is the day's revenue.
class RevenueTrendChart extends StatelessWidget {
  const RevenueTrendChart({super.key, required this.points});

  final List<RevenueTrendPoint> points;

  static const double _height = 240;

  @override
  Widget build(BuildContext context) {
    return DashboardCard(
      title: 'Revenue Trends',
      subtitle: 'Net daily revenue for the selected period',
      child: SizedBox(
        height: _height,
        child: points.isEmpty
            ? const ChartPlaceholder(message: 'No revenue in this period')
            : LineChart(_buildData(context)),
      ),
    );
  }

  LineChartData _buildData(BuildContext context) {
    final spots = <FlSpot>[
      for (var i = 0; i < points.length; i++) FlSpot(i.toDouble(), points[i].amount),
    ];
    final rawInterval = (points.length / 5).floorToDouble();
    final labelInterval = rawInterval < 1 ? 1.0 : rawInterval;

    return LineChartData(
      minY: 0,
      gridData: const FlGridData(show: true, drawVerticalLine: false),
      borderData: FlBorderData(show: false),
      titlesData: FlTitlesData(
        topTitles: const AxisTitles(sideTitles: SideTitles(showTitles: false)),
        rightTitles: const AxisTitles(sideTitles: SideTitles(showTitles: false)),
        leftTitles: AxisTitles(
          sideTitles: SideTitles(
            showTitles: true,
            reservedSize: 48,
            getTitlesWidget: (value, meta) => Text(
              Formatters.money(value),
              style: Theme.of(context).textTheme.bodySmall,
            ),
          ),
        ),
        bottomTitles: AxisTitles(
          sideTitles: SideTitles(
            showTitles: true,
            interval: labelInterval,
            reservedSize: 28,
            getTitlesWidget: (value, meta) {
              final index = value.round();
              if (index < 0 || index >= points.length) return const SizedBox.shrink();
              final date = points[index].dateUtc;
              return Padding(
                padding: const EdgeInsets.only(top: AppSpacing.xxs),
                child: Text(
                  '${date.month}/${date.day}',
                  style: Theme.of(context).textTheme.bodySmall,
                ),
              );
            },
          ),
        ),
      ),
      lineBarsData: [
        LineChartBarData(
          spots: spots,
          isCurved: true,
          color: AppColors.primary,
          barWidth: 2,
          dotData: const FlDotData(show: false),
          belowBarData: BarAreaData(show: true, color: AppColors.primarySoft),
        ),
      ],
    );
  }
}
