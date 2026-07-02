import 'package:courtly/core/enums/reservation_status.dart';
import 'package:courtly/core/enums/time_of_day_bucket.dart';
import 'package:courtly/core/theme/app_theme.dart';
import 'package:courtly/features/reference_data/domain/reference_models.dart'
    show PagedResult;
import 'package:courtly/features/reservations/application/reservation_providers.dart';
import 'package:courtly/features/reservations/data/reservation_api.dart';
import 'package:courtly/features/reservations/data/reservation_repository.dart';
import 'package:courtly/features/reservations/domain/reservation_models.dart';
import 'package:courtly/features/reservations/presentation/client_bookings_screen.dart';
import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

/// F26A (Bookings tab): the list renders the customer's own reservations and the
/// Upcoming/Past segmented control drives the backend query — Upcoming filters by
/// `fromUtc` (slot start ≥ now), Past by `toUtc` (slot start < now). The split is
/// pushed to the server, not computed client-side over a full list.
void main() {
  Reservation booking({
    required int id,
    required String courtName,
    required ReservationStatus status,
  }) {
    final now = DateTime.now().toUtc();
    return Reservation(
      id: id,
      userId: 'u1',
      userName: 'Test User',
      courtId: id,
      courtName: courtName,
      timeSlotId: id * 10,
      slotStartUtc: now.add(const Duration(days: 1)),
      slotEndUtc: now.add(const Duration(days: 1, hours: 1)),
      bucket: TimeOfDayBucket.morning,
      status: status,
      totalPrice: 40,
      isPaid: status == ReservationStatus.confirmed,
      createdAtUtc: now,
    );
  }

  Future<void> pumpBookings(
    WidgetTester tester,
    ReservationRepository repo,
  ) async {
    tester.view.physicalSize = const Size(1000, 2000);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          reservationRepositoryProvider.overrideWithValue(repo),
        ],
        child: MaterialApp(
          theme: AppTheme.light,
          // The client shell normally supplies the Scaffold; wrap for the test.
          home: const Scaffold(body: ClientBookingsScreen()),
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('shows upcoming bookings and queries by fromUtc', (tester) async {
    final repo = _FakeReservationRepository(
      upcoming: [booking(id: 1, courtName: 'Grand Slam Club', status: ReservationStatus.confirmed)],
      past: [booking(id: 2, courtName: 'Riverside Court', status: ReservationStatus.completed)],
    );

    await pumpBookings(tester, repo);

    expect(find.text('Grand Slam Club'), findsOneWidget);
    expect(find.text('Riverside Court'), findsNothing);
    // Upcoming tab → lower-bounded by now, no upper bound.
    expect(repo.lastFromUtc, isNotNull);
    expect(repo.lastToUtc, isNull);
  });

  testWidgets('switching to Past queries by toUtc', (tester) async {
    final repo = _FakeReservationRepository(
      upcoming: [booking(id: 1, courtName: 'Grand Slam Club', status: ReservationStatus.confirmed)],
      past: [booking(id: 2, courtName: 'Riverside Court', status: ReservationStatus.completed)],
    );

    await pumpBookings(tester, repo);
    await tester.tap(find.text('Past'));
    await tester.pumpAndSettle();

    expect(find.text('Riverside Court'), findsOneWidget);
    // Past tab → upper-bounded by now, no lower bound.
    expect(repo.lastToUtc, isNotNull);
    expect(repo.lastFromUtc, isNull);
  });
}

/// Fake repository: returns the [upcoming] page when `listMine` is called with a
/// `fromUtc` bound, the [past] page otherwise; records the last date bounds so
/// the test can assert which tab drove the query.
class _FakeReservationRepository extends ReservationRepository {
  _FakeReservationRepository({required this.upcoming, required this.past})
      : super(ReservationApi(Dio()));

  final List<Reservation> upcoming;
  final List<Reservation> past;
  DateTime? lastFromUtc;
  DateTime? lastToUtc;

  @override
  Future<PagedResult<Reservation>> listMine({
    int page = 1,
    int pageSize = 20,
    int? status,
    DateTime? fromUtc,
    DateTime? toUtc,
  }) async {
    lastFromUtc = fromUtc;
    lastToUtc = toUtc;
    final items = fromUtc != null ? upcoming : past;
    return PagedResult(
      items: items,
      page: page,
      pageSize: pageSize,
      totalCount: items.length,
      hasNext: false,
      hasPrevious: false,
    );
  }
}
