import 'package:flutter/material.dart';

import '../../../../core/theme/app_colors.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../core/utils/formatters.dart';
import '../../../../core/widgets/status_badge.dart';
import '../../../reservations/domain/reservation_models.dart';

/// DISPLAY-ONLY tile for one of the customer's recent bookings on the Home
/// screen (F23): the court name, the formatted slot date/time, and a
/// [StatusBadge] for the reservation status. Intentionally tap-less — opening a
/// reservation lives on the Bookings tab, not here.
class RecentBookingTile extends StatelessWidget {
  const RecentBookingTile({super.key, required this.reservation});

  final Reservation reservation;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final start = reservation.slotStartUtc.toLocal();

    return Container(
      padding: AppSpacing.cardPadding,
      decoration: const BoxDecoration(
        color: AppColors.surface,
        borderRadius: AppSpacing.brLg,
      ),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(
                  reservation.courtName,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: theme.textTheme.titleSmall
                      ?.copyWith(fontWeight: FontWeight.w700),
                ),
                const SizedBox(height: AppSpacing.xxs),
                Text(
                  Formatters.dateTime(start),
                  style: theme.textTheme.bodySmall
                      ?.copyWith(color: AppColors.textSecondary),
                ),
              ],
            ),
          ),
          const SizedBox(width: AppSpacing.sm),
          StatusBadge(
            label: reservation.status.label,
            tone: reservation.status.tone,
          ),
        ],
      ),
    );
  }
}
