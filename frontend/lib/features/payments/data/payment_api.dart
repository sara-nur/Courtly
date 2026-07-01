import 'package:dio/dio.dart';

import '../domain/payment_models.dart';

/// Thin transport over the Feature 16 payment endpoints. Shapes the request and
/// parses the response; failures propagate as [DioException] and the repository
/// normalizes them to a typed `ApiException`.
///
/// Endpoints (must match the backend exactly):
///   POST /api/payments/intent   ({reservationId} → PaymentIntent for the PaymentSheet)
///
/// The webhook (`POST /api/payments/webhook`) is Stripe→server only, and the
/// refund (`POST /api/payments/{id}/refund`) is Admin/Staff — the client calls
/// neither. Payment finalization is server-side; the client never records it.
class PaymentApi {
  PaymentApi(this._dio);

  final Dio _dio;

  static const String _base = '/api/payments';

  /// Creates (or re-fetches) the Stripe PaymentIntent for the caller's Pending,
  /// unpaid reservation. The body carries only the reservation id — the server
  /// verifies ownership from the JWT and computes the amount from the catalog
  /// (never trusted from the client — rubric §7.1).
  Future<PaymentIntentResult> createIntent({required int reservationId}) async {
    final response = await _dio.post<dynamic>(
      '$_base/intent',
      data: <String, dynamic>{'reservationId': reservationId},
    );
    return PaymentIntentResult.fromJson(
      (response.data as Map).cast<String, dynamic>(),
    );
  }
}
