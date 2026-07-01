import 'package:dio/dio.dart';

import '../../../core/network/api_exception.dart';
import '../domain/payment_models.dart';
import 'payment_api.dart';

/// Wraps [PaymentApi] and normalizes every failure to a typed [ApiException]
/// (so the UI sees one error type — including backend business messages like the
/// 409 "reservation is already paid"). Mirrors the API surface one-for-one.
class PaymentRepository {
  PaymentRepository(this._api);

  final PaymentApi _api;

  Future<PaymentIntentResult> createIntent({required int reservationId}) async {
    try {
      return await _api.createIntent(reservationId: reservationId);
    } on DioException catch (e) {
      throw ApiException.from(e);
    }
  }
}
