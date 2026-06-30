import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';

import '../../../../core/theme/app_colors.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../domain/dashboard_models.dart';
import 'chart_placeholder.dart';
import 'dashboard_card.dart';

/// Peak Hours line chart (PRD p.5): how many counted reservations start in each
/// hour of the day (0–23, UTC). The series always carries all 24 hours, so the
/// line is continuous; an all-zero window shows a placeholder instead.
class PeakHoursChart extends StatelessWidget {
  const PeakHoursChart({super.key, required this.points});

  final List<PeakHourPoint> points;

  static const double _height = 240;
  static const double _labelInterval = 3; // label every 3 hours (0, 3, 6 … 21)

  @override
  Widget build(BuildContext context) {
    final hasData = points.any((p) => p.count > 0);
    return DashboardCard(
      title: 'Peak Hours',
      subtitle: 'Bookings by start hour (24h, UTC)',
      child: SizedBox(
        height: _height,
        child: !hasData
            ? const ChartPlaceholder(message: 'No bookings in this period')
            : LineChart(_buildData(context)),
      ),
    );
  }

  LineChartData _buildData(BuildContext context) {
    final sorted = [...points]..sort((a, b) => a.hour.compareTo(b.hour));
    final spots = <FlSpot>[
      for (final point in sorted) FlSpot(point.hour.toDouble(), point.count.toDouble()),
    ];

    return LineChartData(
      minX: 0,
      maxX: 23,
      minY: 0,
      gridData: const FlGridData(show: true, drawVerticalLine: false),
      borderData: FlBorderData(show: false),
      titlesData: FlTitlesData(
        topTitles: const AxisTitles(sideTitles: SideTitles(showTitles: false)),
        rightTitles: const AxisTitles(sideTitles: SideTitles(showTitles: false)),
        leftTitles: const AxisTitles(
          sideTitles: SideTitles(showTitles: true, reservedSize: 32),
        ),
        bottomTitles: AxisTitles(
          sideTitles: SideTitles(
            showTitles: true,
            interval: _labelInterval,
            reservedSize: 28,
            getTitlesWidget: (value, meta) {
              final hour = value.round();
              if (hour < 0 || hour > 23) return const SizedBox.shrink();
              return Padding(
                padding: const EdgeInsets.only(top: AppSpacing.xxs),
                child: Text(
                  '${hour}h',
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
