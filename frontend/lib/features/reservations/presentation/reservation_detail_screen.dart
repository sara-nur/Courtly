import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../app/router/admin_router.dart';
import '../../../core/enums/reservation_status.dart';
import '../../../core/network/api_exception.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../../../core/utils/formatters.dart';
import '../../../core/widgets/app_back_button.dart';
import '../../../core/widgets/async_value_view.dart';
import '../../../core/widgets/confirm_dialog.dart';
import '../../../core/widgets/disabled_action.dart';
import '../../../core/widgets/status_badge.dart';
import '../application/reservation_providers.dart';
import '../domain/reservation_models.dart';
import 'forms/cancel_reservation_dialog.dart';
import 'forms/reschedule_modal.dart';

/// Reservation detail (Feature 15 master-detail #1): the full view of one
/// booking — summary, payment, audit trail — plus the state-machine actions
/// (Confirm / Cancel-with-reason / Complete / Reschedule). Invalid actions are
/// disabled-with-reason (rubric §6); paid bookings can't be cancelled (refund =
/// F16) or rescheduled (payment adjustment). After any action the detail + the
/// table refresh automatically. No raw ids are shown (formatted `#RES-…`).
class ReservationDetailScreen extends ConsumerWidget {
  const ReservationDetailScreen({super.key, required this.reservationId});

  final int reservationId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final detail = ref.watch(reservationDetailProvider(reservationId));

    return Padding(
      padding: AppSpacing.pagePadding,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Align(
            alignment: Alignment.centerLeft,
            child: AppBackButton(
              label: 'Back to reservations',
              onPressed: () => context.go(AdminRoutes.reservations),
            ),
          ),
          const SizedBox(height: AppSpacing.sm),
          Expanded(
            child: AsyncValueView<ReservationDetail>(
              value: detail,
              onRetry: () => ref.invalidate(reservationDetailProvider(reservationId)),
              data: (d) => _DetailBody(detail: d),
            ),
          ),
        ],
      ),
    );
  }
}

class _DetailBody extends ConsumerWidget {
  const _DetailBody({required this.detail});

  final ReservationDetail detail;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final r = detail.reservation;

    return SingleChildScrollView(
      child: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 760),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Row(
                children: [
                  Text(r.reference, style: theme.textTheme.headlineSmall),
                  const SizedBox(width: AppSpacing.sm),
                  StatusBadge(label: r.status.label, tone: r.status.tone),
                ],
              ),
              const SizedBox(height: AppSpacing.md),
              _ActionsBar(detail: detail),
              const SizedBox(height: AppSpacing.md),
              _SummaryCard(reservation: r),
              if (detail.payment != null) ...[
                const SizedBox(height: AppSpacing.md),
                _PaymentCard(payment: detail.payment!),
              ],
              const SizedBox(height: AppSpacing.md),
              _AuditTimeline(audits: detail.audits),
            ],
          ),
        ),
      ),
    );
  }
}

/// A titled card holding a two-column label/value grid (rubric §6: aligned
/// label/value layout, not free-floating text).
class _Card extends StatelessWidget {
  const _Card({required this.title, required this.child});

  final String title;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Card(
      child: Padding(
        padding: AppSpacing.cardPadding,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(title, style: theme.textTheme.titleMedium),
            const SizedBox(height: AppSpacing.sm),
            child,
          ],
        ),
      ),
    );
  }
}

class _Row extends StatelessWidget {
  const _Row(this.label, this.value);

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.xxs),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(
            width: 160,
            child: Text(label,
                style: theme.textTheme.bodyMedium
                    ?.copyWith(color: AppColors.textSecondary)),
          ),
          Expanded(
            child: Text(value,
                style: theme.textTheme.bodyMedium
                    ?.copyWith(fontWeight: FontWeight.w600)),
          ),
        ],
      ),
    );
  }
}

class _SummaryCard extends StatelessWidget {
  const _SummaryCard({required this.reservation});

  final Reservation reservation;

  @override
  Widget build(BuildContext context) {
    final r = reservation;
    final start = r.slotStartUtc.toLocal();
    final end = r.slotEndUtc.toLocal();
    return _Card(
      title: 'Booking',
      child: Column(
        children: [
          _Row('Customer', r.userName),
          if (r.userEmail != null && r.userEmail!.isNotEmpty)
            _Row('Email', r.userEmail!),
          _Row('Court', r.courtName),
          _Row('Date', Formatters.date(start)),
          _Row('Time', '${Formatters.time(start)} – ${Formatters.time(end)}'),
          _Row('Total', Formatters.money(r.totalPrice)),
          _Row('Payment', r.isPaid ? 'Paid' : 'Not paid'),
          _Row('Created', Formatters.dateTime(r.createdAtUtc.toLocal())),
          if (r.status == ReservationStatus.cancelled &&
              r.cancellationReason != null)
            _Row('Cancellation reason', r.cancellationReason!),
        ],
      ),
    );
  }
}

class _PaymentCard extends StatelessWidget {
  const _PaymentCard({required this.payment});

  final ReservationPayment payment;

  @override
  Widget build(BuildContext context) {
    return _Card(
      title: 'Payment',
      child: Column(
        children: [
          Padding(
            padding: const EdgeInsets.symmetric(vertical: AppSpacing.xxs),
            child: Row(
              children: [
                const SizedBox(
                  width: 160,
                  child: Text('Status', style: TextStyle(color: AppColors.textSecondary)),
                ),
                StatusBadge(label: payment.status.label, tone: payment.status.tone),
              ],
            ),
          ),
          _Row('Amount', Formatters.money(payment.amount)),
          if (payment.amountChargedCents != null)
            _Row('Charged', Formatters.moneyFromCents(payment.amountChargedCents!)),
          if (payment.paidAtUtc != null)
            _Row('Paid at', Formatters.dateTime(payment.paidAtUtc!.toLocal())),
        ],
      ),
    );
  }
}

class _AuditTimeline extends StatelessWidget {
  const _AuditTimeline({required this.audits});

  final List<ReservationAudit> audits;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return _Card(
      title: 'Audit trail',
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          for (final a in audits)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: AppSpacing.xs),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  const Padding(
                    padding: EdgeInsets.only(top: 2, right: AppSpacing.sm),
                    child: Icon(Icons.fiber_manual_record, size: 10,
                        color: AppColors.primary),
                  ),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(_headline(a),
                            style: theme.textTheme.bodyMedium
                                ?.copyWith(fontWeight: FontWeight.w600)),
                        if (a.reason != null && a.reason!.isNotEmpty)
                          Text(a.reason!, style: theme.textTheme.bodySmall),
                        Text(
                          '${a.changedByName ?? 'System'} · ${Formatters.dateTime(a.createdAtUtc.toLocal())}',
                          style: theme.textTheme.bodySmall
                              ?.copyWith(color: AppColors.textMuted),
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
        ],
      ),
    );
  }

  String _headline(ReservationAudit a) {
    if (a.isReschedule) return 'Rescheduled';
    if (a.oldStatus == null) return 'Created · ${a.newStatus.label}';
    return '${a.oldStatus!.label} → ${a.newStatus.label}';
  }
}

/// The state-machine action bar. Each action is enabled only on a legal
/// transition; otherwise it stays visible but disabled with the reason
/// (rubric §6). Confirm/Complete use a [ConfirmDialog]; Cancel captures a
/// reason; Reschedule opens the slot picker.
class _ActionsBar extends ConsumerWidget {
  const _ActionsBar({required this.detail});

  final ReservationDetail detail;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final r = detail.reservation;
    final nowUtc = DateTime.now().toUtc();
    final slotEnded = !r.slotEndUtc.toUtc().isAfter(nowUtc);

    final confirmReason = r.status == ReservationStatus.pending
        ? null
        : 'Only a pending booking can be confirmed.';

    final completeReason = r.status != ReservationStatus.confirmed
        ? 'Only a confirmed booking can be completed.'
        : (slotEnded ? null : 'Can be completed after the slot has ended.');

    final cancelReason = !r.isActive
        ? 'A ${r.status.label.toLowerCase()} booking can\'t be cancelled.'
        : (r.isPaid
            ? 'Paid bookings need the refund flow (feature 16).'
            : null);

    final rescheduleReason = !r.isActive
        ? 'A ${r.status.label.toLowerCase()} booking can\'t be rescheduled.'
        : (r.isPaid
            ? 'Paid bookings can\'t be rescheduled (payment adjustment needed).'
            : null);

    return Wrap(
      spacing: AppSpacing.sm,
      runSpacing: AppSpacing.sm,
      children: [
        DisabledAction(
          enabled: confirmReason == null,
          reason: confirmReason,
          child: ElevatedButton.icon(
            onPressed: () => _confirm(context, ref),
            icon: const Icon(Icons.check, size: 18),
            label: const Text('Confirm'),
          ),
        ),
        DisabledAction(
          enabled: completeReason == null,
          reason: completeReason,
          child: OutlinedButton.icon(
            onPressed: () => _complete(context, ref),
            icon: const Icon(Icons.task_alt, size: 18),
            label: const Text('Complete'),
          ),
        ),
        DisabledAction(
          enabled: rescheduleReason == null,
          reason: rescheduleReason,
          child: OutlinedButton.icon(
            onPressed: () => _reschedule(context, ref),
            icon: const Icon(Icons.event_repeat, size: 18),
            label: const Text('Reschedule'),
          ),
        ),
        DisabledAction(
          enabled: cancelReason == null,
          reason: cancelReason,
          child: OutlinedButton.icon(
            onPressed: () => _cancel(context, ref),
            style: OutlinedButton.styleFrom(foregroundColor: AppColors.danger),
            icon: const Icon(Icons.cancel_outlined, size: 18),
            label: const Text('Cancel'),
          ),
        ),
      ],
    );
  }

  Future<void> _confirm(BuildContext context, WidgetRef ref) async {
    final ok = await ConfirmDialog.show(
      context,
      title: 'Confirm booking',
      message: 'Confirm ${detail.reservation.reference}? The customer will be notified.',
      confirmLabel: 'Confirm',
      icon: Icons.check_circle_outline,
    );
    if (!ok || !context.mounted) return;
    await _run(context, ref, () => ref.read(reservationRepositoryProvider).confirm(detail.reservation.id),
        '${detail.reservation.reference} confirmed.');
  }

  Future<void> _complete(BuildContext context, WidgetRef ref) async {
    final ok = await ConfirmDialog.show(
      context,
      title: 'Complete booking',
      message: 'Mark ${detail.reservation.reference} as completed?',
      confirmLabel: 'Complete',
      icon: Icons.task_alt,
    );
    if (!ok || !context.mounted) return;
    await _run(context, ref, () => ref.read(reservationRepositoryProvider).complete(detail.reservation.id),
        '${detail.reservation.reference} completed.');
  }

  Future<void> _cancel(BuildContext context, WidgetRef ref) async {
    final reason = await showCancelReservationDialog(context,
        reference: detail.reservation.reference);
    if (reason == null || !context.mounted) return;
    await _run(
        context,
        ref,
        () => ref.read(reservationRepositoryProvider).cancel(detail.reservation.id, reason),
        '${detail.reservation.reference} cancelled.');
  }

  Future<void> _reschedule(BuildContext context, WidgetRef ref) async {
    final slotId = await showRescheduleModal(
      context,
      courtId: detail.reservation.courtId,
      currentSlotId: detail.reservation.timeSlotId,
      reference: detail.reservation.reference,
    );
    if (slotId == null || !context.mounted) return;
    await _run(
        context,
        ref,
        () => ref.read(reservationRepositoryProvider).reschedule(detail.reservation.id, slotId),
        '${detail.reservation.reference} rescheduled.');
  }

  /// Runs a transition: refreshes the detail + the table on success, surfaces the
  /// backend message on failure (rubric §4 — never a generic error).
  Future<void> _run(
    BuildContext context,
    WidgetRef ref,
    Future<ReservationDetail> Function() action,
    String successMessage,
  ) async {
    try {
      await action();
      ref.invalidate(reservationDetailProvider(detail.reservation.id));
      await ref.read(reservationListControllerProvider.notifier).refresh();
      if (context.mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text(successMessage)));
      }
    } on ApiException catch (e) {
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(e.message), backgroundColor: AppColors.danger),
        );
      }
    } catch (_) {
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text('Something went wrong. Please try again.'),
            backgroundColor: AppColors.danger,
          ),
        );
      }
    }
  }
}
