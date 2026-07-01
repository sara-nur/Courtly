import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../reservations/application/reservation_providers.dart';
import '../../reservations/domain/reservation_models.dart';

/// Drives the **Confirm Booking** action on the client booking screen (F25).
///
/// Calls the client-facing create endpoint (`POST /api/reservations`, owner taken
/// from the JWT, price owned by the server) and exposes the in-flight
/// [AsyncValue] so the button can show a spinner and stay disabled while a create
/// is running. On success it returns the created (Pending) [Reservation] so the
/// screen can route to the confirmation; on failure it holds the error in [state]
/// (e.g. the 409 "slot was just taken") and returns null.
class BookingController extends Notifier<AsyncValue<void>> {
  @override
  AsyncValue<void> build() => const AsyncValue.data(null);

  /// Books [timeSlotId] for the signed-in user. Returns the created reservation
  /// on success, or null on failure (the error stays in [state] for the UI).
  Future<Reservation?> submit(int timeSlotId) async {
    state = const AsyncValue.loading();
    try {
      final detail = await ref
          .read(reservationRepositoryProvider)
          .create(timeSlotId: timeSlotId);
      state = const AsyncValue.data(null);
      return detail.reservation;
    } on Object catch (e, st) {
      state = AsyncValue.error(e, st);
      return null;
    }
  }
}

final bookingControllerProvider =
    NotifierProvider<BookingController, AsyncValue<void>>(BookingController.new);
