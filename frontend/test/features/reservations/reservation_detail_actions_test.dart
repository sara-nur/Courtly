import 'package:courtly/core/enums/payment_status.dart';
import 'package:courtly/core/enums/reservation_status.dart';
import 'package:courtly/core/enums/time_of_day_bucket.dart';
import 'package:courtly/core/widgets/confirm_dialog.dart';
import 'package:courtly/features/reservations/application/reservation_providers.dart';
import 'package:courtly/features/reservations/data/reservation_repository.dart';
import 'package:courtly/features/reservations/domain/reservation_models.dart';
import 'package:courtly/features/reservations/presentation/reservation_detail_screen.dart';
import 'package:courtly/features/reference_data/domain/reference_models.dart'
    show PagedResult;
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

/// Feature 15 DoD (widget): a transition action calls the state-machine API, and
/// an illegal/paid action is disabled-with-reason (rubric §6).
void main() {
  Reservation reservation({
    required ReservationStatus status,
    bool isPaid = false,
  }) =>
      Reservation(
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
        status: status,
        totalPrice: 30,
        isPaid: isPaid,
        createdAtUtc: DateTime.utc(2026, 6, 25, 12, 0),
      );

  ReservationDetail detail(Reservation r, {ReservationPayment? payment}) =>
      ReservationDetail(
        reservation: r,
        audits: [
          ReservationAudit(
            id: 1,
            oldStatus: null,
            newStatus: ReservationStatus.pending,
            createdAtUtc: DateTime.utc(2026, 6, 25, 12, 0),
          ),
        ],
        payment: payment,
      );

  Future<void> pumpDetail(
      WidgetTester tester, _FakeReservationRepository repo) async {
    tester.view.physicalSize = const Size(1200, 1600);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          // The detail provider resolves through the repository's getById, so
          // overriding the repo is enough (no family-element override needed).
          reservationRepositoryProvider.overrideWithValue(repo),
        ],
        child: const MaterialApp(
          home: Scaffold(body: ReservationDetailScreen(reservationId: 1)),
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('Confirm action calls the confirm API after the dialog',
      (tester) async {
    final repo = _FakeReservationRepository(
      detail(reservation(status: ReservationStatus.pending)),
    );
    await pumpDetail(tester, repo);

    // Tap the Confirm action → confirmation dialog → confirm.
    await tester.tap(find.widgetWithText(ElevatedButton, 'Confirm'));
    await tester.pumpAndSettle();
    expect(find.byType(ConfirmDialog), findsOneWidget);

    await tester.tap(find.descendant(
        of: find.byType(ConfirmDialog),
        matching: find.widgetWithText(ElevatedButton, 'Confirm')));
    await tester.pumpAndSettle();

    expect(repo.confirmedId, 1);
  });

  testWidgets('Cancel is disabled-with-reason for a paid reservation',
      (tester) async {
    final paid = ReservationPayment(
      id: 9,
      status: PaymentStatus.succeeded,
      amount: 30,
      amountChargedCents: 3000,
      isPaid: true,
      createdAtUtc: DateTime.utc(2026, 6, 25, 12, 0),
      paidAtUtc: DateTime.utc(2026, 6, 25, 12, 5),
    );
    final repo = _FakeReservationRepository(
      detail(reservation(status: ReservationStatus.confirmed, isPaid: true),
          payment: paid),
    );
    await pumpDetail(tester, repo);

    // The paid-cancel block surfaces as a disabled action with its reason tooltip.
    expect(
      find.byTooltip('Paid bookings need the refund flow (feature 16).'),
      findsOneWidget,
    );
  });
}

class _FakeReservationRepository implements ReservationRepository {
  _FakeReservationRepository(this._result);

  final ReservationDetail _result;
  int? confirmedId;

  @override
  Future<ReservationDetail> getById(int id) async => _result;

  @override
  Future<ReservationDetail> confirm(int id) async {
    confirmedId = id;
    return _result;
  }

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
        items: const [],
        page: 1,
        pageSize: pageSize,
        totalCount: 0,
        hasNext: false,
        hasPrevious: false,
      );

  @override
  dynamic noSuchMethod(Invocation invocation) =>
      throw UnsupportedError('Unexpected call: ${invocation.memberName}');
}
