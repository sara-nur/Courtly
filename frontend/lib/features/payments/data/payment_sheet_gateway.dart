import 'package:flutter_stripe/flutter_stripe.dart';

/// The outcome of presenting the Stripe PaymentSheet. [completed] means the user
/// finished the sheet (the charge is confirmed **at Stripe**) — it is NOT proof
/// of a finalized booking: the server finalizes via the webhook and the client
/// then polls `IsPaid` (rubric §7.1). [canceled] = the user dismissed the sheet;
/// [failed] = the SDK reported an error.
enum PaymentSheetOutcome { completed, canceled, failed }

/// Seam over the `flutter_stripe` PaymentSheet, mirroring the backend's
/// `IStripeGateway` (F16): the payment controller depends only on this interface,
/// so widget tests inject a fake and never touch native Stripe.
abstract class PaymentSheetGateway {
  /// Initializes and presents the PaymentSheet for [clientSecret]. The
  /// [publishableKey] comes from the server's intent response (never hardcoded —
  /// rubric §3.3). [merchantDisplayName] labels the sheet.
  Future<PaymentSheetOutcome> present({
    required String clientSecret,
    required String publishableKey,
    required String merchantDisplayName,
  });
}

/// Production implementation backed by `flutter_stripe`. Sets the publishable key
/// from the server response, initializes the sheet with the PaymentIntent client
/// secret, and presents it. A user dismissal surfaces as [PaymentSheetOutcome.canceled];
/// any other SDK error as [PaymentSheetOutcome.failed].
class StripePaymentSheetGateway implements PaymentSheetGateway {
  const StripePaymentSheetGateway();

  @override
  Future<PaymentSheetOutcome> present({
    required String clientSecret,
    required String publishableKey,
    required String merchantDisplayName,
  }) async {
    // Key is server-sourced; apply it before any PaymentSheet call.
    Stripe.publishableKey = publishableKey;
    await Stripe.instance.applySettings();

    try {
      await Stripe.instance.initPaymentSheet(
        paymentSheetParameters: SetupPaymentSheetParameters(
          paymentIntentClientSecret: clientSecret,
          merchantDisplayName: merchantDisplayName,
        ),
      );
      await Stripe.instance.presentPaymentSheet();
      return PaymentSheetOutcome.completed;
    } on StripeException catch (e) {
      return e.error.code == FailureCode.Canceled
          ? PaymentSheetOutcome.canceled
          : PaymentSheetOutcome.failed;
    }
  }
}
