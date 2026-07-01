import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../../app/router/client_router.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../../../core/utils/formatters.dart';
import '../../../core/widgets/status_badge.dart';
import '../../reservations/domain/reservation_models.dart';

/// The post-confirm screen for F25 (the reservation is created **Pending** — it
/// is not paid yet). Distinct from F26's paid "Booking Confirmed! / Total Paid"
/// receipt: here we show the on-hold booking and prompt the user to pay. F26 will
/// insert the Stripe PaymentSheet *between* Confirm Booking and this screen.
///
/// The created [reservation] rides along as the route's `extra`; if it is missing
/// (e.g. a cold deep-link), we fall back to a short message + return home.
class BookingCreatedScreen extends StatelessWidget {
  const BookingCreatedScreen({super.key, required this.reservation});

  final Reservation? reservation;

  static String _formatDuration(Duration d) {
    final hours = d.inHours;
    final minutes = d.inMinutes % 60;
    if (hours > 0 && minutes > 0) return '${hours}h ${minutes}m';
    if (hours > 0) return '${hours}h';
    return '${minutes}m';
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final r = reservation;

    return Scaffold(
      appBar: AppBar(title: const Text('Booking Created')),
      body: r == null
          ? _MissingReservation(onHome: () => context.go(ClientRoutes.home))
          : ListView(
              padding: const EdgeInsets.all(AppSpacing.lg),
              children: [
                const SizedBox(height: AppSpacing.md),
                Center(
                  child: Container(
                    width: 72,
                    height: 72,
                    decoration: const BoxDecoration(
                      color: AppColors.surfaceMuted,
                      shape: BoxShape.circle,
                    ),
                    child: const Icon(Icons.schedule,
                        size: 36, color: AppColors.warning),
                  ),
                ),
                const SizedBox(height: AppSpacing.md),
                Text(
                  'Booking created',
                  textAlign: TextAlign.center,
                  style: theme.textTheme.headlineSmall
                      ?.copyWith(fontWeight: FontWeight.w800),
                ),
                const SizedBox(height: AppSpacing.xs),
                Text(
                  'Your slot is on hold. Complete payment to confirm your booking.',
                  textAlign: TextAlign.center,
                  style: theme.textTheme.bodyMedium
                      ?.copyWith(color: AppColors.textSecondary),
                ),
                const SizedBox(height: AppSpacing.lg),
                _ReceiptCard(reservation: r, formatDuration: _formatDuration),
                if (r.holdExpiresAtUtc != null) ...[
                  const SizedBox(height: AppSpacing.sm),
                  Text(
                    'Held until ${Formatters.time(r.holdExpiresAtUtc!.toLocal())} — '
                    'pay before then to keep this slot.',
                    textAlign: TextAlign.center,
                    style: theme.textTheme.bodySmall
                        ?.copyWith(color: AppColors.textMuted),
                  ),
                ],
                const SizedBox(height: AppSpacing.xl),
                FilledButton(
                  onPressed: () => context.go(ClientRoutes.home),
                  child: const Text('Done'),
                ),
              ],
            ),
    );
  }
}

/// The digital-receipt card: court, reference, date/time, duration, price and the
/// Pending status badge — a two-column label/value layout (rubric §6). No DB ids.
class _ReceiptCard extends StatelessWidget {
  const _ReceiptCard({required this.reservation, required this.formatDuration});

  final Reservation reservation;
  final String Function(Duration) formatDuration;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final start = reservation.slotStartUtc.toLocal();
    final end = reservation.slotEndUtc.toLocal();

    return Container(
      padding: const EdgeInsets.all(AppSpacing.md),
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: AppSpacing.brLg,
        border: Border.all(color: AppColors.surfaceMuted),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  reservation.courtName,
                  style: theme.textTheme.titleMedium
                      ?.copyWith(fontWeight: FontWeight.w700),
                ),
              ),
              StatusBadge(
                label: reservation.status.label,
                tone: reservation.status.tone,
              ),
            ],
          ),
          const SizedBox(height: AppSpacing.md),
          _ReceiptRow(label: 'Reference', value: reservation.reference),
          _ReceiptRow(label: 'Date', value: Formatters.date(start)),
          _ReceiptRow(
            label: 'Time',
            value: '${Formatters.time(start)} – ${Formatters.time(end)}',
          ),
          _ReceiptRow(
            label: 'Duration',
            value: formatDuration(end.difference(start)),
          ),
          _ReceiptRow(
            label: 'Total price',
            value: Formatters.money(reservation.totalPrice),
            emphasize: true,
          ),
        ],
      ),
    );
  }
}

class _ReceiptRow extends StatelessWidget {
  const _ReceiptRow({
    required this.label,
    required this.value,
    this.emphasize = false,
  });

  final String label;
  final String value;
  final bool emphasize;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.xs),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Expanded(
            child: Text(
              label,
              style:
                  theme.textTheme.bodyMedium?.copyWith(color: AppColors.textMuted),
            ),
          ),
          const SizedBox(width: AppSpacing.md),
          Text(
            value,
            textAlign: TextAlign.right,
            style: theme.textTheme.bodyMedium?.copyWith(
              fontWeight: emphasize ? FontWeight.w800 : FontWeight.w600,
              color: emphasize ? AppColors.primary : AppColors.textPrimary,
            ),
          ),
        ],
      ),
    );
  }
}

class _MissingReservation extends StatelessWidget {
  const _MissingReservation({required this.onHome});

  final VoidCallback onHome;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Center(
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
            FilledButton(onPressed: onHome, child: const Text('Return to Home')),
          ],
        ),
      ),
    );
  }
}
