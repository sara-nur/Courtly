import 'package:flutter/material.dart';

import '../../../../core/theme/app_colors.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../core/utils/formatters.dart';
import '../../../../core/widgets/status_badge.dart';
import '../../domain/reservation_models.dart';

/// One reservation shown as a compact summary row: a calendar glyph, the court
/// name, the formatted slot date/time (UTC → local), and a [StatusBadge]. Shared
/// by the Home "Recent Bookings" strip (F23) and the Bookings tab list (F26A) so
/// both read identically (DRY — rubric §8.1). Never shows a raw DB id.
///
/// When [onTap] is supplied the row becomes tappable (ripple + trailing chevron)
/// and deep-links to the booking detail; without it the row is display-only.
class BookingSummaryTile extends StatelessWidget {
  const BookingSummaryTile({super.key, required this.reservation, this.onTap});

  final Reservation reservation;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final start = reservation.slotStartUtc.toLocal();

    final content = Padding(
      padding: AppSpacing.cardPadding,
      child: Row(
        children: [
          Container(
            width: AppSpacing.avatarSm,
            height: AppSpacing.avatarSm,
            decoration: const BoxDecoration(
              color: AppColors.primarySoft,
              shape: BoxShape.circle,
            ),
            child: const Icon(
              Icons.event_outlined,
              size: 18,
              color: AppColors.primary,
            ),
          ),
          const SizedBox(width: AppSpacing.sm),
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
          if (onTap != null) ...[
            const SizedBox(width: AppSpacing.xxs),
            const Icon(Icons.chevron_right, color: AppColors.textMuted),
          ],
        ],
      ),
    );

    if (onTap == null) {
      return Container(
        decoration: const BoxDecoration(
          color: AppColors.surface,
          borderRadius: AppSpacing.brLg,
        ),
        child: content,
      );
    }

    return Material(
      color: AppColors.surface,
      borderRadius: AppSpacing.brLg,
      clipBehavior: Clip.antiAlias,
      child: InkWell(onTap: onTap, child: content),
    );
  }
}
