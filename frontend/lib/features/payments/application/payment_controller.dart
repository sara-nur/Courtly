import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/network/api_exception.dart';
import '../../client_home/application/home_controller.dart';
import '../../reservations/application/reservation_providers.dart';
import '../data/payment_sheet_gateway.dart';
import 'payment_providers.dart';

/// Where the payment flow is right now. [idle] shows the pay button; the three
/// in-flight phases show a spinner; [paid] is the terminal success (the server
/// confirmed `IsPaid`); [awaitingConfirmation] means Stripe took the payment but
/// the webhook hasn't finalized within the poll window (rare) — the booking will
/// confirm shortly; [failed] surfaces an error and returns the pay button.
enum PaymentPhase {
  idle,
  creatingIntent,
  presentingSheet,
  confirming,
  paid,
  awaitingConfirmation,
  failed,
}

/// Immutable payment-flow state. [message] carries a user-facing note (a cancel
/// notice, an error, or the "confirming" fallback); null otherwise.
class PaymentFlowState {
  const PaymentFlowState(this.phase, {this.message});

  final PaymentPhase phase;
  final String? message;

  bool get isBusy =>
      phase == PaymentPhase.creatingIntent ||
      phase == PaymentPhase.presentingSheet ||
      phase == PaymentPhase.confirming;
}

/// Drives one reservation's payment: create the intent (server owns the amount),
/// present the Stripe PaymentSheet, then **poll the server** until it reports
/// `IsPaid` — success is server truth, never the sheet result (rubric §7.1). On
/// success it invalidates the reservation detail + the home feed so the UI flips
/// to the paid receipt without a manual reload.
class PaymentController extends FamilyNotifier<PaymentFlowState, int> {
  /// How often, and for how long, to poll `GET /api/reservations/{id}` after the
  /// sheet completes, waiting for the webhook to flip `IsPaid`.
  static const Duration _pollInterval = Duration(milliseconds: 1500);
  static const Duration _pollTimeout = Duration(seconds: 20);
  static const String _merchantName = 'Courtly';

  int get _reservationId => arg;

  @override
  PaymentFlowState build(int arg) => const PaymentFlowState(PaymentPhase.idle);

  /// Runs the full pay flow. Safe to call again after a cancel/failure (retry).
  Future<void> pay() async {
    state = const PaymentFlowState(PaymentPhase.creatingIntent);

    final intent = await _createIntent();
    if (intent == null) return; // state already set to failed

    state = const PaymentFlowState(PaymentPhase.presentingSheet);
    final PaymentSheetOutcome outcome;
    try {
      outcome = await ref.read(paymentSheetGatewayProvider).present(
            clientSecret: intent.clientSecret,
            publishableKey: intent.publishableKey,
            merchantDisplayName: _merchantName,
          );
    } on Object {
      state = const PaymentFlowState(
        PaymentPhase.failed,
        message: 'Could not start the payment. Please try again.',
      );
      return;
    }

    switch (outcome) {
      case PaymentSheetOutcome.canceled:
        state = const PaymentFlowState(
          PaymentPhase.idle,
          message: 'Payment canceled.',
        );
      case PaymentSheetOutcome.failed:
        state = const PaymentFlowState(
          PaymentPhase.failed,
          message: 'The payment did not go through. Please try again.',
        );
      case PaymentSheetOutcome.completed:
        await _confirmFromServer();
    }
  }

  Future<_Intent?> _createIntent() async {
    try {
      final result = await ref
          .read(paymentRepositoryProvider)
          .createIntent(reservationId: _reservationId);
      return _Intent(result.clientSecret, result.publishableKey);
    } on Object catch (e) {
      final message = e is ApiException
          ? e.message
          : 'Could not start the payment. Please try again.';
      state = PaymentFlowState(PaymentPhase.failed, message: message);
      return null;
    }
  }

  /// Polls the reservation until the server reports it paid (the webhook has
  /// finalized). Never trusts the client for success.
  Future<void> _confirmFromServer() async {
    state = const PaymentFlowState(PaymentPhase.confirming);

    final repo = ref.read(reservationRepositoryProvider);
    final maxAttempts = (_pollTimeout.inMilliseconds / _pollInterval.inMilliseconds).ceil();

    for (var attempt = 0; attempt < maxAttempts; attempt++) {
      try {
        final detail = await repo.getById(_reservationId);
        if (detail.reservation.isPaid) {
          _refreshViews();
          state = const PaymentFlowState(PaymentPhase.paid);
          return;
        }
      } on Object {
        // Transient read failure — keep polling within the window.
      }
      await Future<void>.delayed(_pollInterval);
    }

    // Payment succeeded at Stripe but the webhook hasn't finalized yet. Refresh
    // once more in case it just landed; otherwise tell the user it'll confirm.
    _refreshViews();
    state = const PaymentFlowState(
      PaymentPhase.awaitingConfirmation,
      message: 'Payment received — confirming your booking. '
          'It will appear in your bookings shortly.',
    );
  }

  void _refreshViews() {
    ref.invalidate(reservationDetailProvider(_reservationId));
    ref.invalidate(homeDataProvider);
  }
}

/// The two fields the sheet needs from the intent response.
class _Intent {
  const _Intent(this.clientSecret, this.publishableKey);
  final String clientSecret;
  final String publishableKey;
}

/// One controller per reservation id.
final paymentControllerProvider =
    NotifierProvider.family<PaymentController, PaymentFlowState, int>(
        PaymentController.new);
