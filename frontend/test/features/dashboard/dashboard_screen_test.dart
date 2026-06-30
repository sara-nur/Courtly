import 'package:courtly/features/court_catalog/application/court_providers.dart';
import 'package:courtly/features/dashboard/application/dashboard_providers.dart';
import 'package:courtly/features/dashboard/data/dashboard_repository.dart';
import 'package:courtly/features/dashboard/domain/dashboard_models.dart';
import 'package:courtly/features/dashboard/presentation/dashboard_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  DashboardMetrics sampleMetrics() => DashboardMetrics(
        fromUtc: DateTime.utc(2026, 5, 26, 12),
        toUtc: DateTime.utc(2026, 6, 25, 12),
        totalReservations: const Kpi(current: 42, previous: 30, deltaPercent: 40),
        revenue: const Kpi(current: 1250, previous: 1000, deltaPercent: 25),
        occupancyRate: const Kpi(current: 68.5, previous: 60, deltaPercent: 14.2),
        activeUsers: const Kpi(current: 18, previous: 20, deltaPercent: -10),
        revenueTrend: [
          RevenueTrendPoint(dateUtc: DateTime.utc(2026, 6, 10), amount: 120),
          RevenueTrendPoint(dateUtc: DateTime.utc(2026, 6, 11), amount: 240),
          RevenueTrendPoint(dateUtc: DateTime.utc(2026, 6, 12), amount: 90),
        ],
        popularCourts: const [
          PopularCourt(courtName: 'Center Court', count: 12, percentage: 50),
          PopularCourt(courtName: 'Court B', count: 6, percentage: 25),
        ],
        peakHours: [
          for (var h = 0; h < 24; h++)
            PeakHourPoint(hour: h, count: h == 10 ? 5 : (h == 18 ? 3 : 0)),
        ],
        health: const HealthCheck(
          status: DashboardHealthStatus.healthy,
          statusName: 'Healthy',
          headline: 'Business is healthy',
          detail: '68.5% occupancy and revenue is up 25% versus the previous period.',
        ),
      );

  Future<void> pumpDashboard(WidgetTester tester, DashboardMetrics metrics) async {
    // Desktop-sized viewport so the KPI grid (4 across) and both chart columns lay out.
    tester.view.physicalSize = const Size(1400, 1800);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          dashboardRepositoryProvider.overrideWithValue(_FakeDashboardRepository(metrics)),
          // Empty court-type lookup so the filter dropdown settles cleanly.
          courtTypeLookupProvider.overrideWith((ref) async => const []),
        ],
        child: const MaterialApp(home: Scaffold(body: DashboardScreen())),
      ),
    );
    await tester.pumpAndSettle();
  }

  // Unmount the tree so the controller's auto-refresh Timer is cancelled (else
  // flutter_test fails the test for a still-pending timer).
  Future<void> disposeDashboard(WidgetTester tester) async {
    await tester.pumpWidget(const SizedBox());
    await tester.pump();
  }

  testWidgets('renders the health banner, KPI cards and chart sections', (tester) async {
    await pumpDashboard(tester, sampleMetrics());

    expect(find.text('Dashboard Overview'), findsOneWidget);

    // KPI cards (labels + values).
    expect(find.text('Total Reservations'), findsOneWidget);
    expect(find.text('42'), findsOneWidget);
    expect(find.text('Revenue'), findsOneWidget);
    expect(find.text('Occupancy Rate'), findsOneWidget);
    expect(find.text('68.5%'), findsOneWidget);
    expect(find.text('Active Users'), findsOneWidget);
    expect(find.text('18'), findsOneWidget);

    // Health banner + chart section titles + a popular court (by name, no id).
    expect(find.text('Business is healthy'), findsOneWidget);
    expect(find.text('Revenue Trends'), findsOneWidget);
    expect(find.text('Most Popular Courts'), findsOneWidget);
    expect(find.text('Peak Hours'), findsOneWidget);
    expect(find.text('Center Court'), findsOneWidget);

    await disposeDashboard(tester);
  });

  testWidgets('shows the prior-period delta and the court-type filter', (tester) async {
    await pumpDashboard(tester, sampleMetrics());

    // Total Reservations grew 40% → the delta chip shows it.
    expect(find.text('40.0%'), findsOneWidget);
    expect(find.text('vs prior period'), findsWidgets);

    // The DB-fed court-type filter is present (rubric: dropdowns, not free text).
    expect(find.text('Court type'), findsOneWidget);
    expect(find.text('From date'), findsOneWidget);
    expect(find.text('To date'), findsOneWidget);

    await disposeDashboard(tester);
  });
}

class _FakeDashboardRepository implements DashboardRepository {
  _FakeDashboardRepository(this.metrics);

  final DashboardMetrics metrics;

  @override
  Future<DashboardMetrics> getMetrics({
    DateTime? fromUtc,
    DateTime? toUtc,
    int? courtTypeId,
  }) async =>
      metrics;
}
