import 'package:courtly/core/enums/reservation_status.dart';
import 'package:courtly/core/enums/time_of_day_bucket.dart';
import 'package:courtly/core/widgets/status_badge.dart';
import 'package:courtly/features/reservations/application/reservation_providers.dart';
import 'package:courtly/features/reservations/data/reservation_repository.dart';
import 'package:courtly/features/reservations/domain/reservation_models.dart';
import 'package:courtly/features/reservations/presentation/reservations_screen.dart';
import 'package:courtly/features/reference_data/domain/reference_models.dart'
    show PagedResult;
import 'package:courtly/features/users/application/user_providers.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

/// Feature 15 DoD (widget): the reservation table renders rows (reference,
/// customer, status badge) from the repository and shows the paged footer.
void main() {
  final reservation = Reservation(
    id: 1,
    userId: 'u-1',
    userName: 'Alex Johnson',
    userEmail: 'alex@courtly.test',
    courtId: 7,
    courtName: 'Court A',
    timeSlotId: 30,
    slotStartUtc: DateTime.utc(2026, 7, 1, 8, 0),
    slotEndUtc: DateTime.utc(2026, 7, 1, 9, 0),
    bucket: TimeOfDayBucket.morning,
    status: ReservationStatus.pending,
    totalPrice: 30,
    isPaid: false,
    createdAtUtc: DateTime.utc(2026, 6, 25, 12, 0),
  );

  Future<void> pumpScreen(WidgetTester tester, List<Reservation> rows) async {
    tester.view.physicalSize = const Size(1200, 1600);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          reservationRepositoryProvider
              .overrideWithValue(_FakeReservationRepository(rows)),
          // Empty lookups settle the filter bar + "+ New Booking" gate cleanly.
          reservationCourtLookupProvider.overrideWith((ref) async => const []),
          userLookupProvider.overrideWith((ref) async => const []),
        ],
        child: const MaterialApp(home: Scaffold(body: ReservationsScreen())),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('renders a reservation row with reference, customer and status',
      (tester) async {
    await pumpScreen(tester, [reservation]);

    expect(find.text('#RES-001'), findsOneWidget);
    expect(find.text('Alex Johnson'), findsOneWidget);
    expect(find.text('alex@courtly.test'), findsOneWidget);
    expect(find.text('Court A'), findsOneWidget);
    // The status renders as a badge (not just text), proving the enum→badge map.
    expect(
      find.descendant(
          of: find.byType(StatusBadge), matching: find.text('Pending')),
      findsOneWidget,
    );
    // Paged footer (dash glyph is the widget's; match on the stable prefix/suffix).
    expect(find.textContaining('Showing'), findsOneWidget);
    expect(find.textContaining('of 1'), findsOneWidget);
  });

  testWidgets('shows the empty placeholder when there are no reservations',
      (tester) async {
    await pumpScreen(tester, const []);

    expect(find.text('No reservations match these filters'), findsOneWidget);
  });
}

/// Minimal fake — only `list` is exercised by the table; any other call is an
/// explicit test failure (mirrors the court screen test's fake pattern).
class _FakeReservationRepository implements ReservationRepository {
  _FakeReservationRepository(this.rows);

  final List<Reservation> rows;

  @override
  Future<PagedResult<Reservation>> list({
    int page = 1,
    int pageSize = 20,
    ReservationStatus? status,
    int? courtId,
    String? userId,
    DateTime? fromUtc,
    DateTime? toUtc,
  }) async =>
      PagedResult<Reservation>(
        items: rows,
        page: 1,
        pageSize: pageSize,
        totalCount: rows.length,
        hasNext: false,
        hasPrevious: false,
      );

  @override
  dynamic noSuchMethod(Invocation invocation) =>
      throw UnsupportedError('Unexpected call: ${invocation.memberName}');
}
