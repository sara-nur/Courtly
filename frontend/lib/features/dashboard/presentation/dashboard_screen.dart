import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/theme/app_spacing.dart';
import '../../../core/utils/formatters.dart';
import '../../../core/widgets/async_value_view.dart';
import '../../../core/widgets/date_time_picker_field.dart';
import '../../../core/widgets/db_dropdown.dart';
import '../../court_catalog/application/court_providers.dart';
import '../../reference_data/domain/reference_models.dart';
import '../application/dashboard_providers.dart';
import '../domain/dashboard_models.dart';
import 'widgets/health_banner.dart';
import 'widgets/kpi_card.dart';
import 'widgets/peak_hours_chart.dart';
import 'widgets/popular_courts_chart.dart';
import 'widgets/revenue_trend_chart.dart';

/// Dashboard Overview (Feature 19, mockup p.5): the health banner, 4 KPI cards
/// (each with a ▲/▼% delta vs the prior period), and three charts — Revenue
/// Trends, Most Popular Courts and Peak Hours — over a date-range + court-type
/// filter. Data auto-refreshes on a timer (no manual refresh); no raw ids shown.
class DashboardScreen extends ConsumerWidget {
  const DashboardScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final state = ref.watch(dashboardControllerProvider);
    final controller = ref.read(dashboardControllerProvider.notifier);

    return Padding(
      padding: AppSpacing.pagePadding,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text('Dashboard Overview', style: theme.textTheme.headlineSmall),
          const SizedBox(height: AppSpacing.xxs),
          Text(
            "Welcome back. Here's how the business is performing.",
            style: theme.textTheme.bodyMedium,
          ),
          const SizedBox(height: AppSpacing.md),
          _FilterBar(filters: state.filters, controller: controller),
          const SizedBox(height: AppSpacing.md),
          Expanded(
            child: AsyncValueView<DashboardMetrics>(
              value: state.value,
              onRetry: controller.load,
              data: (metrics) => _DashboardBody(metrics: metrics),
            ),
          ),
        ],
      ),
    );
  }
}

/// The resolved dashboard content (scrolls vertically; the KPI grid and the
/// popular-courts/peak-hours pair reflow on narrower widths).
class _DashboardBody extends StatelessWidget {
  const _DashboardBody({required this.metrics});

  final DashboardMetrics metrics;

  @override
  Widget build(BuildContext context) {
    return SingleChildScrollView(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          HealthBanner(health: metrics.health),
          const SizedBox(height: AppSpacing.md),
          _KpiGrid(metrics: metrics),
          const SizedBox(height: AppSpacing.md),
          RevenueTrendChart(points: metrics.revenueTrend),
          const SizedBox(height: AppSpacing.md),
          LayoutBuilder(
            builder: (context, constraints) {
              final popular = PopularCourtsChart(courts: metrics.popularCourts);
              final peak = PeakHoursChart(points: metrics.peakHours);
              if (constraints.maxWidth >= 900) {
                return Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Expanded(child: popular),
                    const SizedBox(width: AppSpacing.md),
                    Expanded(child: peak),
                  ],
                );
              }
              return Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [popular, const SizedBox(height: AppSpacing.md), peak],
              );
            },
          ),
        ],
      ),
    );
  }
}

/// The 4 KPI cards laid out as a responsive grid (4 across on desktop, fewer on
/// narrower widths).
class _KpiGrid extends StatelessWidget {
  const _KpiGrid({required this.metrics});

  final DashboardMetrics metrics;

  @override
  Widget build(BuildContext context) {
    final cards = <Widget>[
      KpiCard(
        label: 'Total Reservations',
        value: metrics.totalReservations.current.toInt().toString(),
        icon: Icons.event_available_outlined,
        kpi: metrics.totalReservations,
      ),
      KpiCard(
        label: 'Revenue',
        value: Formatters.money(metrics.revenue.current),
        icon: Icons.payments_outlined,
        kpi: metrics.revenue,
      ),
      KpiCard(
        label: 'Occupancy Rate',
        value: '${metrics.occupancyRate.current.toStringAsFixed(1)}%',
        icon: Icons.donut_large_outlined,
        kpi: metrics.occupancyRate,
      ),
      KpiCard(
        label: 'Active Users',
        value: metrics.activeUsers.current.toInt().toString(),
        icon: Icons.group_outlined,
        kpi: metrics.activeUsers,
      ),
    ];

    return LayoutBuilder(
      builder: (context, constraints) {
        final columns = constraints.maxWidth >= 1000
            ? 4
            : constraints.maxWidth >= 600
                ? 2
                : 1;
        const spacing = AppSpacing.md;
        final itemWidth = (constraints.maxWidth - spacing * (columns - 1)) / columns;
        return Wrap(
          spacing: spacing,
          runSpacing: spacing,
          children: [
            for (final card in cards) SizedBox(width: itemWidth, child: card),
          ],
        );
      },
    );
  }
}

/// Filter row: a from/to date range + a DB-fed court-type dropdown, plus a clear
/// action. Mirrors the F15 reservation filter bar (a reflowing [Wrap]).
class _FilterBar extends ConsumerWidget {
  const _FilterBar({required this.filters, required this.controller});

  final DashboardFilters filters;
  final DashboardController controller;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final courtTypes = ref.watch(courtTypeLookupProvider);

    return Wrap(
      spacing: AppSpacing.md,
      runSpacing: AppSpacing.sm,
      crossAxisAlignment: WrapCrossAlignment.center,
      children: [
        SizedBox(
          width: 200,
          child: DateTimePickerField(
            label: 'From date',
            value: filters.fromUtc?.toLocal(),
            includeTime: false,
            onChanged: (value) =>
                controller.setFilters(filters.copyWith(fromUtc: _dayStartUtc(value))),
          ),
        ),
        SizedBox(
          width: 200,
          child: DateTimePickerField(
            label: 'To date',
            value: filters.toUtc?.toLocal().subtract(const Duration(days: 1)),
            includeTime: false,
            onChanged: (value) =>
                controller.setFilters(filters.copyWith(toUtc: _dayEndUtc(value))),
          ),
        ),
        SizedBox(
          width: 220,
          child: DbDropdown<CourtType?>(
            label: 'Court type',
            value: courtTypes.maybeWhen(
              data: (list) => _courtTypeById(list, filters.courtTypeId),
              orElse: () => null,
            ),
            items: [
              null,
              ...courtTypes.maybeWhen(data: (list) => list, orElse: () => const <CourtType>[]),
            ],
            itemLabel: (type) => type?.name ?? 'All court types',
            onChanged: (type) => controller.setFilters(
              type == null
                  ? filters.copyWith(clearCourtType: true)
                  : filters.copyWith(courtTypeId: type.id),
            ),
          ),
        ),
        if (filters.isActive)
          TextButton.icon(
            onPressed: controller.clearFilters,
            icon: const Icon(Icons.clear_all, size: 18),
            label: const Text('Clear'),
          ),
      ],
    );
  }

  static CourtType? _courtTypeById(List<CourtType> list, int? id) {
    if (id == null) return null;
    for (final type in list) {
      if (type.id == id) return type;
    }
    return null;
  }

  /// Start of the picked local day, as a UTC instant (inclusive range start).
  static DateTime _dayStartUtc(DateTime localDay) =>
      DateTime.utc(localDay.year, localDay.month, localDay.day);

  /// Start of the day AFTER the picked local day, as UTC (exclusive range end,
  /// so the picked day itself is included).
  static DateTime _dayEndUtc(DateTime localDay) =>
      DateTime.utc(localDay.year, localDay.month, localDay.day).add(const Duration(days: 1));
}
