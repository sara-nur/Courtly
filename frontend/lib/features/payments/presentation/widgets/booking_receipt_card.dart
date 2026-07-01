import 'package:flutter/material.dart';

import '../../../../core/theme/app_colors.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../core/utils/formatters.dart';
import '../../../../core/widgets/status_badge.dart';
import '../../../reservations/domain/reservation_models.dart';

/// The digital-receipt card shared by both payment states (F26): court, booking
/// reference, date, time range, duration, and the total — a two-column
/// label/value layout (rubric §6). Never shows a raw DB id. [priceLabel] switches
/// between the pre-payment "Total price" and the paid "Total Paid".
///
/// Extracted from F25's booking-created screen so the unpaid ("pay") and paid
/// ("confirmed") states render one consistent receipt (DRY — rubric §8.1).
class BookingReceiptCard extends StatelessWidget {
  const BookingReceiptCard({
    super.key,
    required this.reservation,
    this.priceLabel = 'Total price',
  });

  final Reservation reservation;
  final String priceLabel;

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
            value: _formatDuration(end.difference(start)),
          ),
          _ReceiptRow(
            label: priceLabel,
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
