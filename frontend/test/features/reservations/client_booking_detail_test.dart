import 'package:courtly/core/enums/payment_status.dart';
import 'package:courtly/core/enums/reservation_status.dart';
import 'package:courtly/core/enums/time_of_day_bucket.dart';
import 'package:courtly/core/theme/app_theme.dart';
import 'package:courtly/features/reference_data/domain/reference_models.dart'
    show PagedResult;
import 'package:courtly/features/reservations/application/reservation_providers.dart';
import 'package:courtly/features/reservations/data/reservation_api.dart';
import 'package:courtly/features/reservations/data/reservation_repository.dart';
import 'package:courtly/features/reservations/domain/reservation_models.dart';
import 'package:courtly/features/reservations/presentation/client_booking_detail_screen.dart';
import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

/// F26A (booking detail): the DoD widget test. The receipt renders; Cancel is
/// **disabled with a reason** on a paid booking (the backend blocks a self-cancel
/// of a paid reservation — refund is admin-only) and **enabled** on an unpaid
/// Pending booking, where confirming the reason dialog posts the cancel.
void main() {
  const reservationId = 42;
  const paidCancelReason =
      'Paid bookings can’t be cancelled here — contact support for a refund.';

  ReservationDetail detailWith({
    required ReservationStatus status,
    required bool isPaid,
    ReservationPayment? payment,
  }) {
    final now = DateTime.now().toUtc();
    return ReservationDetail(
      reservation: Reservation(
        id: reservationId,
        userId: 'u1',
        userName: 'Test User',
        courtId: 3,
        courtName: 'Grand Slam Court',
        timeSlotId: 101,
        slotStartUtc: now.add(const Duration(days: 2)),
        slotEndUtc: now.add(const Duration(days: 2, hours: 1)),
        bucket: TimeOfDayBucket.afternoon,
        status: status,
        totalPrice: 45,
        isPaid: isPaid,
        createdAtUtc: now,
      ),
      audits: const [],
      payment: payment,
    );
  }

  Future<_FakeReservationRepository> pumpDetail(
    WidgetTester tester,
    ReservationDetail detail,
  ) async {
    tester.view.physicalSize = const Size(1000, 2200);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    final repo = _FakeReservationRepository(detail);
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          reservationRepositoryProvider.overrideWithValue(repo),
        ],
        child: MaterialApp(
          theme: AppTheme.light,
          home: const ClientBookingDetailScreen(reservationId: reservationId),
        ),
      ),
    );
    await tester.pumpAndSettle();
    return repo;
  }

  testWidgets('paid booking shows the receipt and a disabled cancel + reason',
      (tester) async {
    final now = DateTime.now().toUtc();
    await pumpDetail(
      tester,
      detailWith(
        status: ReservationStatus.confirmed,
        isPaid: true,
        payment: ReservationPayment(
          id: 1,
          status: PaymentStatus.succeeded,
          amount: 45,
          amountChargedCents: 4500,
          isPaid: true,
          createdAtUtc: now,
          paidAtUtc: now,
        ),
      ),
    );

    // Receipt in the paid state + the explained, unavailable cancel.
    expect(find.text('Total Paid'), findsOneWidget);
    expect(find.text('Cancel booking'), findsOneWidget);
    expect(find.text(paidCancelReason), findsOneWidget);
  });

  testWidgets('unpaid Pending booking cancels via the reason dialog',
      (tester) async {
    final repo = await pumpDetail(
      tester,
      detailWith(status: ReservationStatus.pending, isPaid: false),
    );

    // Enabled: no blocked-reason line is shown.
    expect(find.text(paidCancelReason), findsNothing);

    // Open the cancellation reason dialog.
    await tester.tap(find.widgetWithText(OutlinedButton, 'Cancel booking'));
    await tester.pumpAndSettle();
    expect(find.text('Keep booking'), findsOneWidget); // the dialog is open

    // A reason is required; enter one and confirm.
    await tester.enterText(find.byType(TextField), 'Change of plans');
    await tester.tap(find.widgetWithText(ElevatedButton, 'Cancel booking'));
    await tester.pumpAndSettle();

    expect(repo.cancelCalls, 1);
    expect(repo.lastCancelReason, 'Change of plans');
  });
}

/// Fake repository: serves the seeded detail, records the cancel call, and
/// returns empty pages for the Bookings list refresh the cancel triggers.
class _FakeReservationRepository extends ReservationRepository {
  _FakeReservationRepository(this._detail) : super(ReservationApi(Dio()));

  final ReservationDetail _detail;
  int cancelCalls = 0;
  String? lastCancelReason;

  @override
  Future<ReservationDetail> getById(int id) async => _detail;

  @override
  Future<ReservationDetail> cancel(int id, String reason) async {
    cancelCalls++;
    lastCancelReason = reason;
    return _detail;
  }

  @override
  Future<PagedResult<Reservation>> listMine({
    int page = 1,
    int pageSize = 20,
    int? status,
    DateTime? fromUtc,
    DateTime? toUtc,
  }) async =>
      const PagedResult(
        items: [],
        page: 1,
        pageSize: 10,
        totalCount: 0,
        hasNext: false,
        hasPrevious: false,
      );
}
