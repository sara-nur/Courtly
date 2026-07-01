import 'package:courtly/app/router/client_router.dart';
import 'package:courtly/core/app_flavor.dart';
import 'package:courtly/core/enums/reservation_status.dart';
import 'package:courtly/core/enums/time_of_day_bucket.dart';
import 'package:courtly/core/env/app_config.dart';
import 'package:courtly/core/theme/app_theme.dart';
import 'package:courtly/features/client_booking/presentation/client_booking_screen.dart';
import 'package:courtly/features/court_catalog/application/court_providers.dart';
import 'package:courtly/features/court_catalog/domain/court_models.dart';
import 'package:courtly/features/reservations/application/reservation_providers.dart';
import 'package:courtly/features/reservations/data/reservation_api.dart';
import 'package:courtly/features/reservations/data/reservation_repository.dart';
import 'package:courtly/features/reservations/domain/reservation_models.dart';
import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';

/// F25 (client booking): the slot grid renders bucketed slots with the mockup's
/// states (a taken slot is disabled+labelled and can't be picked), selecting a
/// free slot surfaces its server price in the sticky bar, and **Confirm Booking**
/// (through the confirm dialog) creates the reservation exactly once with the
/// selected slot id, then routes to the confirmation.
void main() {
  const court = Court(
    id: 1,
    name: 'Grand Slam Court',
    description: 'A premier hard court for testing.',
    cityId: 1,
    cityName: 'Santa Monica',
    countryId: 1,
    countryName: 'USA',
    surfaceTypeId: 1,
    surfaceTypeName: 'Hard',
    courtTypeId: 1,
    courtTypeName: 'Tennis',
    isIndoor: true,
    isActive: true,
    isFeatured: true,
    hourlyPrice: 45,
  );

  // Slots two days out, so none is disabled as "Past".
  final base = DateTime.now().add(const Duration(days: 2));
  DateTime slotStart(int hour) => DateTime(base.year, base.month, base.day, hour);

  final availableSlot = AvailabilitySlot(
    id: 101,
    startUtc: slotStart(14).toUtc(),
    endUtc: slotStart(15).toUtc(),
    price: 30,
    isTaken: false,
  );
  final takenSlot = AvailabilitySlot(
    id: 102,
    startUtc: slotStart(15).toUtc(),
    endUtc: slotStart(16).toUtc(),
    price: 30,
    isTaken: true,
  );

  final testDay = DayAvailability(
    courtId: 1,
    date: DateTime(base.year, base.month, base.day),
    isCourtUnderMaintenance: false,
    buckets: [
      AvailabilityBucket(
        bucket: TimeOfDayBucket.afternoon,
        bucketName: 'Afternoon',
        slots: [availableSlot, takenSlot],
      ),
    ],
  );

  GoRouter buildRouter() => GoRouter(
        initialLocation: ClientRoutes.bookingPath(1),
        routes: [
          GoRoute(
            path: '/courts/:id/booking',
            builder: (context, state) => ClientBookingScreen(
              courtId: int.parse(state.pathParameters['id']!),
            ),
          ),
          GoRoute(
            path: ClientRoutes.bookingCreated,
            builder: (context, state) =>
                const Scaffold(body: Text('created-screen')),
          ),
        ],
      );

  Future<void> pumpBooking(
    WidgetTester tester, {
    required ReservationRepository repo,
  }) async {
    tester.view.physicalSize = const Size(1200, 2600);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          appConfigProvider.overrideWithValue(
            const AppConfig(
              flavor: AppFlavor.client,
              apiBaseUrl: 'http://10.0.2.2:5000',
            ),
          ),
          courtByIdProvider.overrideWith((ref, id) async => court),
          slotAvailabilityProvider.overrideWith((ref, key) async => testDay),
          reservationRepositoryProvider.overrideWithValue(repo),
        ],
        child: MaterialApp.router(
          theme: AppTheme.light,
          routerConfig: buildRouter(),
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('renders bucketed slots; a taken slot is disabled and not selectable',
      (tester) async {
    await pumpBooking(tester, repo: _FakeReservationRepository());

    // Court header + bucket + both slots render.
    expect(find.text('Grand Slam Court'), findsOneWidget);
    expect(find.text('Afternoon'), findsOneWidget);
    expect(find.text('2:00 PM'), findsOneWidget);
    expect(find.text('3:00 PM'), findsOneWidget);
    expect(find.text('Available'), findsOneWidget);
    expect(find.text('Taken'), findsOneWidget);

    // Nothing selected yet.
    expect(find.text('Select a time slot'), findsOneWidget);

    // Tapping the taken slot must not select it.
    await tester.tap(find.text('3:00 PM'));
    await tester.pump();
    expect(find.text('Select a time slot'), findsOneWidget);
  });

  testWidgets('selecting a free slot shows its price and confirming books it once',
      (tester) async {
    final repo = _FakeReservationRepository();
    await pumpBooking(tester, repo: repo);

    // Pick the free 2:00 PM slot → sticky bar shows the server price.
    await tester.tap(find.text('2:00 PM'));
    await tester.pump();
    expect(find.text('Select a time slot'), findsNothing);
    expect(find.text('Selected'), findsOneWidget);
    expect(find.text(r'$30.00'), findsOneWidget);

    // Confirm Booking → confirm dialog → confirm.
    await tester.tap(find.text('Confirm Booking'));
    await tester.pumpAndSettle();
    expect(find.text('Confirm your booking'), findsOneWidget);

    await tester.tap(find.text('Confirm booking'));
    await tester.pumpAndSettle();

    // Created exactly once with the selected slot id, then routed onward.
    expect(repo.createCalls, 1);
    expect(repo.lastSlotId, 101);
    expect(find.text('created-screen'), findsOneWidget);
  });
}

/// Records `create` calls and returns a Pending reservation, without touching the
/// network (the injected [ReservationApi]/[Dio] are never exercised).
class _FakeReservationRepository extends ReservationRepository {
  _FakeReservationRepository() : super(ReservationApi(Dio()));

  int createCalls = 0;
  int? lastSlotId;

  @override
  Future<ReservationDetail> create({required int timeSlotId}) async {
    createCalls++;
    lastSlotId = timeSlotId;
    final now = DateTime.now().toUtc();
    return ReservationDetail(
      reservation: Reservation(
        id: 500,
        userId: 'u1',
        userName: 'Test User',
        courtId: 1,
        courtName: 'Grand Slam Court',
        timeSlotId: timeSlotId,
        slotStartUtc: now.add(const Duration(days: 2)),
        slotEndUtc: now.add(const Duration(days: 2, hours: 1)),
        bucket: TimeOfDayBucket.afternoon,
        status: ReservationStatus.pending,
        totalPrice: 30,
        isPaid: false,
        createdAtUtc: now,
      ),
      audits: const [],
    );
  }
}
