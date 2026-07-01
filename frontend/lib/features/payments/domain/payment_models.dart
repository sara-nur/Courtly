/// Client-side payment models (Feature 26). Mirror the backend payment DTOs
/// (camelCase JSON keys). The server owns the amount end-to-end — the client
/// sends only a reservation id and receives what the in-app Stripe PaymentSheet
/// needs; it never sends or trusts a price, and never records success itself
/// (rubric §7.1).
library;

/// What the client needs to confirm a charge in-app: the PaymentIntent
/// [clientSecret], the [publishableKey] for the Stripe SDK (supplied by the
/// server so it is never hardcoded — rubric §3.3), and the server-owned
/// [amountCents]/[currency] for display. [paymentId] is our payment row's id.
///
/// The response of `POST /api/reservations`'s sibling `POST /api/payments/intent`.
class PaymentIntentResult {
  const PaymentIntentResult({
    required this.paymentId,
    required this.reservationId,
    required this.clientSecret,
    required this.publishableKey,
    required this.amountCents,
    required this.currency,
  });

  final int paymentId;
  final int reservationId;
  final String clientSecret;
  final String publishableKey;

  /// Server-owned charge amount in the smallest currency unit (e.g. cents).
  final int amountCents;
  final String currency;

  factory PaymentIntentResult.fromJson(Map<String, dynamic> json) =>
      PaymentIntentResult(
        paymentId: (json['paymentId'] as num).toInt(),
        reservationId: (json['reservationId'] as num).toInt(),
        clientSecret: json['clientSecret'] as String? ?? '',
        publishableKey: json['publishableKey'] as String? ?? '',
        amountCents: (json['amountCents'] as num?)?.toInt() ?? 0,
        currency: json['currency'] as String? ?? 'usd',
      );
}
