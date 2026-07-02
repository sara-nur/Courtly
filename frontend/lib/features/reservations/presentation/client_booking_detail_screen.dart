import 'package:add_2_calendar/add_2_calendar.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/enums/reservation_status.dart';
import '../../../core/network/api_exception.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../../../core/utils/formatters.dart';
import '../../../core/widgets/async_value_view.dart';
import '../../../core/widgets/disabled_action.dart';
import '../../../core/widgets/status_badge.dart';
import '../../client_home/application/home_controller.dart';
import '../../court_detail/application/court_detail_providers.dart';
import '../../court_detail/presentation/widgets/write_review_sheet.dart';
import '../../payments/presentation/widgets/booking_receipt_card.dart';
import '../../reviews/application/review_providers.dart';
import '../../reviews/domain/review_models.dart';
import '../application/client_bookings_controller.dart';
import '../application/reservation_providers.dart';
import '../domain/reservation_models.dart';
import 'forms/cancel_reservation_dialog.dart';

/// Booking detail + receipt (F26A). Reached by tapping a row on the Bookings tab
/// or the Home "Recent Bookings" strip. Shows the digital receipt (court,
/// date/time, duration, total, status, IsPaid) plus the payment/cancellation
/// summary, and the per-booking actions: **Cancel** (disabled-with-reason when
/// the backend won't allow a self-cancel), **Add to Calendar**, and **Leave a
/// review** once the booking is Completed. The app bar carries the standard back
/// button (rubric §6). The live `reservationDetailProvider` is the source of
/// truth so the screen reflects the latest status after an action.
class ClientBookingDetailScreen extends ConsumerWidget {
  const ClientBookingDetailScreen({super.key, required this.reservationId});

  final int reservationId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final value = ref.watch(reservationDetailProvider(reservationId));

    return Scaffold(
      appBar: AppBar(title: const Text('Booking')),
      body: AsyncValueView<ReservationDetail>(
        value: value,
        onRetry: () => ref.invalidate(reservationDetailProvider(reservationId)),
        data: (detail) => _DetailBody(detail: detail),
      ),
    );
  }
}

class _DetailBody extends StatelessWidget {
  const _DetailBody({required this.detail});

  final ReservationDetail detail;

  @override
  Widget build(BuildContext context) {
    final r = detail.reservation;
    final payment = detail.payment;

    return ListView(
      padding: const EdgeInsets.all(AppSpacing.lg),
      children: [
        BookingReceiptCard(
          reservation: r,
          priceLabel: r.isPaid ? 'Total Paid' : 'Total price',
        ),
        if (payment != null) ...[
          const SizedBox(height: AppSpacing.md),
          _PaymentSummary(payment: payment),
        ],
        if (r.status == ReservationStatus.cancelled) ...[
          const SizedBox(height: AppSpacing.md),
          _CancellationPanel(reservation: r),
        ],
        const SizedBox(height: AppSpacing.lg),
        _BookingActions(reservation: r),
      ],
    );
  }
}

/// A compact "Payment" row: the payment's status badge plus, once settled, when
/// it was paid — so a paid or refunded booking reads at a glance.
class _PaymentSummary extends StatelessWidget {
  const _PaymentSummary({required this.payment});

  final ReservationPayment payment;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final paidAt = payment.paidAtUtc?.toLocal();

    return Container(
      padding: const EdgeInsets.all(AppSpacing.md),
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: AppSpacing.brLg,
        border: Border.all(color: AppColors.surfaceMuted),
      ),
      child: Row(
        children: [
          Text(
            'Payment',
            style: theme.textTheme.bodyMedium?.copyWith(color: AppColors.textMuted),
          ),
          const Spacer(),
          if (paidAt != null) ...[
            Text(
              Formatters.dateTime(paidAt),
              style: theme.textTheme.bodySmall
                  ?.copyWith(color: AppColors.textSecondary),
            ),
            const SizedBox(width: AppSpacing.sm),
          ],
          StatusBadge(label: payment.status.label, tone: payment.status.tone),
        ],
      ),
    );
  }
}

/// Explains a cancelled booking: the recorded reason and when it was cancelled
/// (rubric §7 — a cancellation carries a reason).
class _CancellationPanel extends StatelessWidget {
  const _CancellationPanel({required this.reservation});

  final Reservation reservation;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final cancelledAt = reservation.cancelledAtUtc?.toLocal();
    final reason = reservation.cancellationReason;
    final danger = StatusToneColors.of(StatusTone.danger);

    return Container(
      padding: const EdgeInsets.all(AppSpacing.md),
      decoration: BoxDecoration(
        color: danger.background,
        borderRadius: AppSpacing.brLg,
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(Icons.cancel_outlined, size: 18, color: danger.foreground),
              const SizedBox(width: AppSpacing.xs),
              Text(
                cancelledAt == null
                    ? 'Booking cancelled'
                    : 'Cancelled on ${Formatters.dateTime(cancelledAt)}',
                style: theme.textTheme.bodyMedium?.copyWith(
                    fontWeight: FontWeight.w700, color: danger.foreground),
              ),
            ],
          ),
          if (reason != null && reason.isNotEmpty) ...[
            const SizedBox(height: AppSpacing.xs),
            Text(
              reason,
              style: theme.textTheme.bodyMedium
                  ?.copyWith(color: AppColors.textSecondary),
            ),
          ],
        ],
      ),
    );
  }
}

/// The action buttons for one booking. Holds the in-flight/reviewed state, so it
/// is a stateful consumer: Cancel (self-cancel of an unpaid, still-active
/// booking), Add to Calendar (active bookings), and Leave a review (Completed).
class _BookingActions extends ConsumerStatefulWidget {
  const _BookingActions({required this.reservation});

  final Reservation reservation;

  @override
  ConsumerState<_BookingActions> createState() => _BookingActionsState();
}

class _BookingActionsState extends ConsumerState<_BookingActions> {
  bool _busy = false;
  bool _reviewed = false;

  Reservation get _r => widget.reservation;

  /// Why a self-cancel is unavailable, or null when it is allowed. The backend
  /// only lets a customer cancel an *unpaid* booking that is still active; a paid
  /// booking needs the admin refund flow, and terminal bookings can't change.
  String? get _cancelBlockedReason {
    final r = _r;
    if (r.status == ReservationStatus.cancelled) {
      return 'This booking is already cancelled.';
    }
    if (r.status == ReservationStatus.completed) {
      return 'Completed bookings can’t be cancelled.';
    }
    if (r.isPaid) {
      return 'Paid bookings can’t be cancelled here — contact support for a refund.';
    }
    return null;
  }

  Future<void> _cancel() async {
    final r = _r;
    final reason = await showCancelReservationDialog(context, reference: r.reference);
    if (reason == null) return;

    setState(() => _busy = true);
    try {
      await ref.read(reservationRepositoryProvider).cancel(r.id, reason);
      // Reflect the new status everywhere it shows, with no manual refresh
      // (rubric §6): this detail, the Home strip, and the Bookings list.
      ref.invalidate(reservationDetailProvider(r.id));
      ref.invalidate(myRecentReservationsProvider);
      ref.invalidate(homeDataProvider);
      await ref.read(clientBookingsControllerProvider.notifier).refresh();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Booking ${r.reference} cancelled.')),
        );
      }
    } on ApiException catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text(e.message)));
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _addToCalendar() {
    final r = _r;
    final event = Event(
      title: 'Court booking — ${r.courtName}',
      description: 'Courtly reservation ${r.reference}',
      location: r.courtName,
      startDate: r.slotStartUtc.toLocal(),
      endDate: r.slotEndUtc.toLocal(),
    );
    return Add2Calendar.addEvent2Cal(event);
  }

  Future<void> _leaveReview() async {
    final r = _r;
    final review = await showModalBottomSheet<Review>(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.transparent,
      builder: (_) => WriteReviewSheet(reservationId: r.id, courtName: r.courtName),
    );
    if (review == null) return;

    setState(() => _reviewed = true);
    // Refresh the court's rating/reviews and this detail after posting.
    ref.invalidate(reservationDetailProvider(r.id));
    ref.invalidate(courtDetailProvider(r.courtId));
    ref.invalidate(courtReviewsPageProvider);
    if (mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Thanks for your review!')),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final r = _r;
    final blockedReason = _cancelBlockedReason;
    final canCancel = blockedReason == null && !_busy;
    final canReview = r.status == ReservationStatus.completed && !_reviewed;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        if (canReview) ...[
          FilledButton.icon(
            onPressed: _busy ? null : _leaveReview,
            icon: const Icon(Icons.rate_review_outlined),
            label: const Text('Leave a review'),
          ),
          const SizedBox(height: AppSpacing.sm),
        ],
        if (r.isActive) ...[
          OutlinedButton.icon(
            onPressed: _busy ? null : () => _addToCalendar(),
            icon: const Icon(Icons.event_available_outlined),
            label: const Text('Add to Calendar'),
          ),
          const SizedBox(height: AppSpacing.sm),
        ],
        DisabledAction(
          enabled: canCancel,
          reason: blockedReason,
          child: OutlinedButton.icon(
            onPressed: _cancel,
            icon: _busy
                ? const SizedBox(
                    width: 18,
                    height: 18,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : const Icon(Icons.cancel_outlined, color: AppColors.danger),
            label: const Text('Cancel booking'),
            style: OutlinedButton.styleFrom(foregroundColor: AppColors.danger),
          ),
        ),
        // The Tooltip on a disabled action is hard to reach on mobile, so also
        // surface the reason as a visible line below the button (rubric §6).
        if (blockedReason != null) ...[
          const SizedBox(height: AppSpacing.xs),
          Text(
            blockedReason,
            textAlign: TextAlign.center,
            style: theme.textTheme.bodySmall?.copyWith(color: AppColors.textMuted),
          ),
        ],
      ],
    );
  }
}
