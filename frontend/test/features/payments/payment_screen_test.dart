import 'package:courtly/core/app_flavor.dart';
import 'package:courtly/core/enums/reservation_status.dart';
import 'package:courtly/core/enums/time_of_day_bucket.dart';
import 'package:courtly/core/env/app_config.dart';
import 'package:courtly/core/theme/app_theme.dart';
import 'package:courtly/features/payments/application/payment_providers.dart';
import 'package:courtly/features/payments/data/payment_api.dart';
import 'package:courtly/features/payments/data/payment_repository.dart';
import 'package:courtly/features/payments/data/payment_sheet_gateway.dart';
import 'package:courtly/features/payments/domain/payment_models.dart';
import 'package:courtly/features/payments/presentation/payment_screen.dart';
import 'package:courtly/features/reservations/application/reservation_providers.dart';
import 'package:courtly/features/reservations/data/reservation_api.dart';
import 'package:courtly/features/reservations/data/reservation_repository.dart';
import 'package:courtly/features/reservations/domain/reservation_models.dart';
import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

/// F26 (client payment): the DoD widget test. A **paid** reservation renders the
/// green "Booking Confirmed!" receipt with Total Paid + Add-to-Calendar and hides
/// the pay button (rubric §7.1). An **unpaid** reservation shows "Pay $X"; paying
/// (confirm dialog → fake PaymentSheet completes → server reports IsPaid on the
/// poll) flips the screen to the paid state and removes the pay button — success
/// is server truth, never the sheet result.
void main() {
  const reservationId = 500;

  Reservation reservationWith({required bool isPaid, required ReservationStatus status}) {
    final now = DateTime.now().toUtc();
    return Reservation(
      id: reservationId,
      userId: 'u1',
      userName: 'Test User',
      courtId: 1,
      courtName: 'Grand Slam Court',
      timeSlotId: 101,
      slotStartUtc: now.add(const Duration(days: 2)),
      slotEndUtc: now.add(const Duration(days: 2, hours: 1)),
      bucket: TimeOfDayBucket.afternoon,
      status: status,
      totalPrice: 30,
      isPaid: isPaid,
      createdAtUtc: now,
    );
  }

  Future<void> pumpPayment(
    WidgetTester tester, {
    required Reservation seed,
    required ReservationRepository reservationRepo,
    PaymentRepository? paymentRepo,
    PaymentSheetGateway? gateway,
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
          reservationRepositoryProvider.overrideWithValue(reservationRepo),
          if (paymentRepo != null)
            paymentRepositoryProvider.overrideWithValue(paymentRepo),
          if (gateway != null)
            paymentSheetGatewayProvider.overrideWithValue(gateway),
        ],
        child: MaterialApp(
          theme: AppTheme.light,
          home: PaymentScreen(reservation: seed),
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('paid reservation shows the confirmed receipt and no pay button',
      (tester) async {
    await pumpPayment(
      tester,
      seed: reservationWith(isPaid: true, status: ReservationStatus.confirmed),
      reservationRepo: _FakeReservationRepository(paid: true),
    );

    expect(find.text('Booking Confirmed!'), findsOneWidget);
    expect(find.text('Total Paid'), findsOneWidget);
    expect(find.text('Add to Calendar'), findsOneWidget);
    // The pay button is gone (rubric §7.1 — hidden once IsPaid).
    expect(find.text(r'Pay $30.00'), findsNothing);
  });

  testWidgets('unpaid reservation pays, then flips to the paid state', (tester) async {
    final repo = _FakeReservationRepository(paid: false);
    final paymentRepo = _FakePaymentRepository();
    // The fake sheet "succeeds" and, like the real webhook, makes the server
    // report the reservation as paid on the next poll.
    final gateway = _FakePaymentSheetGateway(() => repo.paid = true);

    await pumpPayment(
      tester,
      seed: reservationWith(isPaid: false, status: ReservationStatus.pending),
      reservationRepo: repo,
      paymentRepo: paymentRepo,
      gateway: gateway,
    );

    // Unpaid: the pay button is shown.
    expect(find.text(r'Pay $30.00'), findsOneWidget);
    expect(find.text('Booking Confirmed!'), findsNothing);

    // Tap Pay → confirm dialog → confirm.
    await tester.tap(find.text(r'Pay $30.00'));
    await tester.pumpAndSettle();
    expect(find.text('Confirm payment'), findsOneWidget);

    await tester.tap(find.text('Pay now'));
    await tester.pumpAndSettle();

    // Intent created once; the screen flipped to the paid receipt, pay button gone.
    expect(paymentRepo.intentCalls, 1);
    expect(gateway.presentCalls, 1);
    expect(find.text('Booking Confirmed!'), findsOneWidget);
    expect(find.text('Total Paid'), findsOneWidget);
    expect(find.text(r'Pay $30.00'), findsNothing);
  });
}

/// Fake reservation repository: `getById` returns a reservation whose paid flag is
/// [paid] (flipped by the fake gateway to simulate the webhook finalizing).
class _FakeReservationRepository extends ReservationRepository {
  _FakeReservationRepository({required this.paid}) : super(ReservationApi(Dio()));

  bool paid;

  @override
  Future<ReservationDetail> getById(int id) async {
    final now = DateTime.now().toUtc();
    return ReservationDetail(
      reservation: Reservation(
        id: id,
        userId: 'u1',
        userName: 'Test User',
        courtId: 1,
        courtName: 'Grand Slam Court',
        timeSlotId: 101,
        slotStartUtc: now.add(const Duration(days: 2)),
        slotEndUtc: now.add(const Duration(days: 2, hours: 1)),
        bucket: TimeOfDayBucket.afternoon,
        status: paid ? ReservationStatus.confirmed : ReservationStatus.pending,
        totalPrice: 30,
        isPaid: paid,
        createdAtUtc: now,
      ),
      audits: const [],
    );
  }
}

/// Fake payment repository: records intent calls; returns a canned intent.
class _FakePaymentRepository extends PaymentRepository {
  _FakePaymentRepository() : super(PaymentApi(Dio()));

  int intentCalls = 0;

  @override
  Future<PaymentIntentResult> createIntent({required int reservationId}) async {
    intentCalls++;
    return PaymentIntentResult(
      paymentId: 1,
      reservationId: reservationId,
      clientSecret: 'cs_test',
      publishableKey: 'pk_test',
      amountCents: 3000,
      currency: 'usd',
    );
  }
}

/// Fake PaymentSheet gateway: runs [onPresent] (to simulate the server finalizing)
/// and reports a completed outcome, without touching native Stripe.
class _FakePaymentSheetGateway implements PaymentSheetGateway {
  _FakePaymentSheetGateway(this.onPresent);

  final void Function() onPresent;
  int presentCalls = 0;

  @override
  Future<PaymentSheetOutcome> present({
    required String clientSecret,
    required String publishableKey,
    required String merchantDisplayName,
  }) async {
    presentCalls++;
    onPresent();
    return PaymentSheetOutcome.completed;
  }
}
