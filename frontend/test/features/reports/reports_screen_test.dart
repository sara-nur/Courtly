import 'package:courtly/features/court_catalog/domain/court_models.dart';
import 'package:courtly/features/reports/application/reports_providers.dart';
import 'package:courtly/features/reports/domain/report_models.dart';
import 'package:courtly/features/reports/presentation/reports_screen.dart';
import 'package:courtly/features/reservations/application/reservation_providers.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

ReservationsReportData _sampleReservations() => ReservationsReportData(
      fromUtc: DateTime.utc(2026, 6, 1),
      toUtc: DateTime.utc(2026, 7, 1),
      courtLabel: 'All courts',
      statusLabel: 'All statuses',
      rows: [
        ReservationReportRow(
          reference: '#RES-001',
          customer: 'Alex Johnson',
          email: 'alex@courtly.test',
          courtName: 'Center Court',
          slotStartUtc: DateTime.utc(2026, 6, 10, 10),
          slotEndUtc: DateTime.utc(2026, 6, 10, 11),
          statusName: 'Confirmed',
          amount: 30,
          isPaid: true,
        ),
      ],
      statusBreakdown: const [ReportStatusCount(statusName: 'Confirmed', count: 1)],
      totalCount: 1,
      totalAmount: 30,
      generatedAtUtc: DateTime.utc(2026, 6, 30),
    );

RevenueUtilizationReportData _sampleRevenue() => RevenueUtilizationReportData(
      year: 2026,
      month: 6,
      monthLabel: 'June 2026',
      courtLabel: 'All courts',
      rows: const [
        CourtUtilizationRow(courtName: 'Center Court', bookings: 2, revenue: 75, utilizationPct: 50),
      ],
      totalBookings: 2,
      totalRevenue: 75,
      overallUtilizationPct: 50,
      generatedAtUtc: DateTime.utc(2026, 6, 30),
    );

void main() {
  Future<void> pumpReports(WidgetTester tester) async {
    tester.view.physicalSize = const Size(1400, 1600);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          reservationsReportDataProvider.overrideWith((ref, filters) async => _sampleReservations()),
          revenueReportDataProvider.overrideWith((ref, filters) async => _sampleRevenue()),
          reservationCourtLookupProvider.overrideWith((ref) async => const <Court>[]),
        ],
        child: const MaterialApp(home: Scaffold(body: ReportsScreen())),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('renders the reservations report as a table with totals and filters', (tester) async {
    await pumpReports(tester);

    // Header + both tabs.
    expect(find.text('Reports'), findsOneWidget);
    expect(find.text('Reservations'), findsWidgets);
    expect(find.text('Revenue & utilisation'), findsOneWidget);

    // The reservations table renders the row data (reference, customer, status, court).
    expect(find.text('#RES-001'), findsOneWidget);
    expect(find.text('Alex Johnson'), findsOneWidget);
    expect(find.text('Center Court'), findsOneWidget);
    expect(find.text('Confirmed'), findsWidgets);

    // Totals summary + the filters + the PDF action.
    expect(find.textContaining('Total bookings'), findsWidgets);
    expect(find.text('From date'), findsWidgets);
    expect(find.text('Status'), findsWidgets); // a DropdownButtonFormField renders its label more than once
    expect(find.text('Preview & print PDF'), findsOneWidget);
  });
}
