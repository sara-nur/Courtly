import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/network/dio_client.dart';
import '../data/payment_api.dart';
import '../data/payment_repository.dart';
import '../data/payment_sheet_gateway.dart';

/// Transport over the payment endpoints, bound to the app Dio client.
final paymentApiProvider = Provider<PaymentApi>(
  (ref) => PaymentApi(ref.watch(dioProvider)),
);

/// The payment repository (normalizes failures to `ApiException`).
final paymentRepositoryProvider = Provider<PaymentRepository>(
  (ref) => PaymentRepository(ref.watch(paymentApiProvider)),
);

/// The Stripe PaymentSheet gateway. Overridden with a fake in widget tests so
/// the payment flow is exercised without native Stripe.
final paymentSheetGatewayProvider = Provider<PaymentSheetGateway>(
  (ref) => const StripePaymentSheetGateway(),
);
