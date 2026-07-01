import 'package:add_2_calendar/add_2_calendar.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../app/router/client_router.dart';
import '../../../core/enums/reservation_status.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../../../core/utils/formatters.dart';
import '../../../core/widgets/confirm_dialog.dart';
import '../../reservations/application/reservation_providers.dart';
import '../../reservations/domain/reservation_models.dart';
import '../application/payment_controller.dart';
import 'widgets/booking_receipt_card.dart';

/// The client payment + confirmation screen (F26, mockup p.10 §4 + p.9 §3.2.4).
/// A single state-driven screen keyed by reservation:
///
/// * **Unpaid** — the receipt + a sticky **"Pay $X"** button that confirms
///   (rubric §6) then drives the Stripe PaymentSheet and polls the server.
/// * **Paid** — flips in place to a green **"Booking Confirmed!"** receipt with
///   **Total Paid**, **Add to Calendar**, and **Return to Home**; the pay button
///   is gone (rubric §7.1 — `IsPaid` hides it).
///
/// The paid state is driven by the server's `IsPaid` (from `reservationDetailProvider`),
/// never by the PaymentSheet result — the client never records success.
class PaymentScreen extends ConsumerWidget {
  const PaymentScreen({super.key, required this.reservation});

  /// The just-created reservation, passed as the route `extra` for an instant
  /// render; the live `reservationDetailProvider` is the source of truth.
  final Reservation? reservation;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final seed = reservation;
    if (seed == null) {
      return const _MissingReservationScaffold();
    }

    final id = seed.id;
    // Server truth (Pending→Confirmed, IsPaid) with the seed as an instant
    // fallback while the first fetch resolves.
    final detail = ref.watch(reservationDetailProvider(id)).valueOrNull;
    final r = detail?.reservation ?? seed;
    final flow = ref.watch(paymentControllerProvider(id));

    // Surface cancel/failure notices as a snackbar (paid/awaiting states render
    // their own inline messages).
    ref.listen<PaymentFlowState>(paymentControllerProvider(id), (prev, next) {
      final message = next.message;
      final isNotice = next.phase == PaymentPhase.failed ||
          (next.phase == PaymentPhase.idle && prev?.phase != PaymentPhase.idle);
      if (message != null && isNotice) {
        ScaffoldMessenger.of(context)
          ..hideCurrentSnackBar()
          ..showSnackBar(SnackBar(content: Text(message)));
      }
    });

    final isPaid = r.isPaid || flow.phase == PaymentPhase.paid;
    if (isPaid) {
      return _PaidView(reservation: r);
    }
    if (r.status == ReservationStatus.cancelled) {
      return const _CancelledView();
    }
    return _UnpaidView(reservation: r, flow: flow);
  }
}

/// The pre-payment state: receipt + hold-expiry note + a sticky "Pay $X" bar.
class _UnpaidView extends ConsumerWidget {
  const _UnpaidView({required this.reservation, required this.flow});

  final Reservation reservation;
  final PaymentFlowState flow;

  Future<void> _pay(BuildContext context, WidgetRef ref) async {
    final start = reservation.slotStartUtc.toLocal();
    final confirmed = await ConfirmDialog.show(
      context,
      title: 'Confirm payment',
      message:
          'Pay ${Formatters.money(reservation.totalPrice)} for ${reservation.courtName} '
          'on ${Formatters.date(start)} at ${Formatters.time(start)}?\n\n'
          'Your card will be charged now to confirm the booking.',
      confirmLabel: 'Pay now',
      icon: Icons.lock_outline,
    );
    if (!confirmed) return;
    await ref.read(paymentControllerProvider(reservation.id).notifier).pay();
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final awaiting = flow.phase == PaymentPhase.awaitingConfirmation;

    return Scaffold(
      appBar: AppBar(title: const Text('Payment')),
      body: ListView(
        padding: const EdgeInsets.all(AppSpacing.lg),
        children: [
          const SizedBox(height: AppSpacing.sm),
          Center(
            child: Container(
              width: 72,
              height: 72,
              decoration: const BoxDecoration(
                color: AppColors.surfaceMuted,
                shape: BoxShape.circle,
              ),
              child: const Icon(Icons.lock_outline,
                  size: 34, color: AppColors.primary),
            ),
          ),
          const SizedBox(height: AppSpacing.md),
          Text(
            'Complete your payment',
            textAlign: TextAlign.center,
            style: theme.textTheme.headlineSmall
                ?.copyWith(fontWeight: FontWeight.w800),
          ),
          const SizedBox(height: AppSpacing.xs),
          Text(
            'Your slot is on hold. Pay now to confirm the booking.',
            textAlign: TextAlign.center,
            style:
                theme.textTheme.bodyMedium?.copyWith(color: AppColors.textSecondary),
          ),
          const SizedBox(height: AppSpacing.lg),
          BookingReceiptCard(reservation: reservation),
          if (reservation.holdExpiresAtUtc != null && !awaiting) ...[
            const SizedBox(height: AppSpacing.sm),
            Text(
              'Held until ${Formatters.time(reservation.holdExpiresAtUtc!.toLocal())} — '
              'pay before then to keep this slot.',
              textAlign: TextAlign.center,
              style: theme.textTheme.bodySmall?.copyWith(color: AppColors.textMuted),
            ),
          ],
          if (awaiting) ...[
            const SizedBox(height: AppSpacing.md),
            _InfoPanel(message: flow.message ?? ''),
            const SizedBox(height: AppSpacing.md),
            OutlinedButton.icon(
              onPressed: () =>
                  ref.invalidate(reservationDetailProvider(reservation.id)),
              icon: const Icon(Icons.refresh),
              label: const Text('Refresh status'),
            ),
            TextButton(
              onPressed: () => context.go(ClientRoutes.home),
              child: const Text('Return to Home'),
            ),
          ],
        ],
      ),
      bottomNavigationBar:
          awaiting ? null : _PayBar(flow: flow, reservation: reservation, onPay: () => _pay(context, ref)),
    );
  }
}

/// The sticky "Pay $X" bar. Disabled + spinner while the flow is in flight; its
/// label tracks the step (processing the sheet vs confirming on the server).
class _PayBar extends StatelessWidget {
  const _PayBar({required this.flow, required this.reservation, required this.onPay});

  final PaymentFlowState flow;
  final Reservation reservation;
  final Future<void> Function() onPay;

  @override
  Widget build(BuildContext context) {
    final busy = flow.isBusy;
    final label = switch (flow.phase) {
      PaymentPhase.confirming => 'Confirming payment…',
      PaymentPhase.creatingIntent || PaymentPhase.presentingSheet => 'Processing…',
      _ => 'Pay ${Formatters.money(reservation.totalPrice)}',
    };

    return Material(
      color: AppColors.surface,
      elevation: 8,
      child: SafeArea(
        top: false,
        child: Padding(
          padding: const EdgeInsets.all(AppSpacing.lg),
          child: SizedBox(
            width: double.infinity,
            child: FilledButton.icon(
              onPressed: busy ? null : () => onPay(),
              icon: busy
                  ? const SizedBox(
                      width: 18,
                      height: 18,
                      child: CircularProgressIndicator(
                          strokeWidth: 2, color: Colors.white),
                    )
                  : const Icon(Icons.lock_outline),
              label: Text(label),
            ),
          ),
        ),
      ),
    );
  }
}

/// The paid state: green check, "Booking Confirmed!", the receipt with Total
/// Paid, Add-to-Calendar and Return-to-Home. No pay button (rubric §7.1).
class _PaidView extends StatelessWidget {
  const _PaidView({required this.reservation});

  final Reservation reservation;

  Future<void> _addToCalendar() {
    final event = Event(
      title: 'Court booking — ${reservation.courtName}',
      description: 'Courtly reservation ${reservation.reference}',
      location: reservation.courtName,
      startDate: reservation.slotStartUtc.toLocal(),
      endDate: reservation.slotEndUtc.toLocal(),
    );
    return Add2Calendar.addEvent2Cal(event);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Scaffold(
      appBar: AppBar(title: const Text('Booking Confirmed')),
      body: ListView(
        padding: const EdgeInsets.all(AppSpacing.lg),
        children: [
          const SizedBox(height: AppSpacing.md),
          Center(
            child: Container(
              width: 80,
              height: 80,
              decoration: const BoxDecoration(
                color: Color(0xFFDCFCE7), // success tint
                shape: BoxShape.circle,
              ),
              child: const Icon(Icons.check_circle,
                  size: 44, color: AppColors.success),
            ),
          ),
          const SizedBox(height: AppSpacing.md),
          Text(
            'Booking Confirmed!',
            textAlign: TextAlign.center,
            style: theme.textTheme.headlineSmall
                ?.copyWith(fontWeight: FontWeight.w800),
          ),
          const SizedBox(height: AppSpacing.xs),
          Text(
            'Your payment went through and your court is booked.',
            textAlign: TextAlign.center,
            style:
                theme.textTheme.bodyMedium?.copyWith(color: AppColors.textSecondary),
          ),
          const SizedBox(height: AppSpacing.lg),
          BookingReceiptCard(reservation: reservation, priceLabel: 'Total Paid'),
          const SizedBox(height: AppSpacing.lg),
          OutlinedButton.icon(
            onPressed: () {
              _addToCalendar();
            },
            icon: const Icon(Icons.event_available_outlined),
            label: const Text('Add to Calendar'),
          ),
          const SizedBox(height: AppSpacing.sm),
          FilledButton(
            onPressed: () => context.go(ClientRoutes.home),
            child: const Text('Return to Home'),
          ),
        ],
      ),
    );
  }
}

/// Shown when the reservation is already cancelled (e.g. an expired hold) — no
/// payment is possible; explain why and route home (rubric §6 disabled + reason).
class _CancelledView extends StatelessWidget {
  const _CancelledView();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Scaffold(
      appBar: AppBar(title: const Text('Payment')),
      body: Center(
        child: Padding(
          padding: const EdgeInsets.all(AppSpacing.xl),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Icon(Icons.event_busy, color: AppColors.textMuted, size: 40),
              const SizedBox(height: AppSpacing.sm),
              Text(
                'This booking was cancelled, so it can’t be paid. '
                'Please make a new booking.',
                textAlign: TextAlign.center,
                style: theme.textTheme.bodyMedium
                    ?.copyWith(color: AppColors.textSecondary),
              ),
              const SizedBox(height: AppSpacing.md),
              FilledButton(
                onPressed: () => context.go(ClientRoutes.home),
                child: const Text('Return to Home'),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// A soft info panel for the "payment received — confirming" fallback.
class _InfoPanel extends StatelessWidget {
  const _InfoPanel({required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Container(
      padding: const EdgeInsets.all(AppSpacing.md),
      decoration: BoxDecoration(
        color: AppColors.primarySoft,
        borderRadius: AppSpacing.brLg,
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Icon(Icons.hourglass_top, color: AppColors.primaryDark, size: 20),
          const SizedBox(width: AppSpacing.sm),
          Expanded(
            child: Text(
              message,
              style: theme.textTheme.bodyMedium
                  ?.copyWith(color: AppColors.primaryDark),
            ),
          ),
        ],
      ),
    );
  }
}

/// Fallback when the screen is opened without a reservation (e.g. a cold
/// deep-link) — a short message + return home.
class _MissingReservationScaffold extends StatelessWidget {
  const _MissingReservationScaffold();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Scaffold(
      appBar: AppBar(title: const Text('Payment')),
      body: Center(
        child: Padding(
          padding: const EdgeInsets.all(AppSpacing.xl),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Icon(Icons.info_outline, color: AppColors.textMuted),
              const SizedBox(height: AppSpacing.sm),
              Text(
                'This booking is no longer available to show here.',
                textAlign: TextAlign.center,
                style: theme.textTheme.bodyMedium
                    ?.copyWith(color: AppColors.textSecondary),
              ),
              const SizedBox(height: AppSpacing.md),
              FilledButton(
                onPressed: () => context.go(ClientRoutes.home),
                child: const Text('Return to Home'),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
