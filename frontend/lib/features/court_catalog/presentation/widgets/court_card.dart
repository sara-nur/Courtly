import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/env/app_config.dart';
import '../../../../core/theme/app_colors.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../core/utils/formatters.dart';
import '../../../../core/utils/image_urls.dart';
import '../../../../core/widgets/status_badge.dart';
import '../../domain/court_models.dart';

/// A grid card for one [Court]: the seeded image (graceful placeholder when
/// none), the court name, its surface + indoor/outdoor and city, the formatted
/// hourly rate, Active/Inactive + (optional) Featured badges, and
/// Maintenance/Edit/Delete affordances. No raw ids are ever shown (rubric).
///
/// Feature 12 adds the status badge: a court with an OPEN maintenance window
/// shows a **Maintenance** badge + "Unavailable for reservations", and the
/// wrench action opens the maintenance modal (status, scheduling, Fix, history).
///
/// Deferred fields (popularity, Reserved) are intentionally NOT rendered — they
/// belong to later features.
class CourtCard extends StatelessWidget {
  const CourtCard({
    super.key,
    required this.court,
    required this.onEdit,
    required this.onDelete,
    required this.onManageMaintenance,
  });

  final Court court;
  final VoidCallback onEdit;
  final VoidCallback onDelete;
  final VoidCallback onManageMaintenance;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Card(
      clipBehavior: Clip.antiAlias,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        mainAxisSize: MainAxisSize.min,
        children: [
          _CourtImage(imageUrl: court.primaryImageUrl),
          Padding(
            padding: AppSpacing.cardPadding,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(
                  court.name,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: theme.textTheme.titleMedium
                      ?.copyWith(fontWeight: FontWeight.w600),
                ),
                const SizedBox(height: AppSpacing.xxs),
                Text(
                  _subtitle,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: theme.textTheme.bodySmall
                      ?.copyWith(color: AppColors.textSecondary),
                ),
                const SizedBox(height: AppSpacing.sm),
                Text(
                  '${Formatters.money(court.hourlyPrice)} / hour',
                  style: theme.textTheme.titleSmall
                      ?.copyWith(color: AppColors.primary),
                ),
                const SizedBox(height: AppSpacing.sm),
                Wrap(
                  spacing: AppSpacing.xs,
                  runSpacing: AppSpacing.xs,
                  children: [
                    // Maintenance takes precedence over the green "Active": effective availability is
                    // active AND not under maintenance. "Inactive" still shows — it is a separate, persistent reason.
                    if (court.isUnderMaintenance)
                      const StatusBadge(
                          label: 'Maintenance', tone: StatusTone.warning),
                    if (!court.isUnderMaintenance && court.isActive)
                      const StatusBadge(label: 'Active', tone: StatusTone.success),
                    if (!court.isActive)
                      const StatusBadge(label: 'Inactive', tone: StatusTone.neutral),
                    if (court.isFeatured)
                      const StatusBadge(label: 'Featured', tone: StatusTone.info),
                  ],
                ),
                if (court.isUnderMaintenance) ...[
                  const SizedBox(height: AppSpacing.xxs),
                  Text(
                    'Unavailable for reservations',
                    style: theme.textTheme.bodySmall
                        ?.copyWith(color: AppColors.textMuted),
                  ),
                ],
                const SizedBox(height: AppSpacing.xs),
                Row(
                  mainAxisAlignment: MainAxisAlignment.end,
                  children: [
                    IconButton(
                      tooltip: 'Maintenance & status',
                      icon: const Icon(Icons.build_outlined),
                      color: court.isUnderMaintenance
                          ? AppColors.warning
                          : AppColors.textSecondary,
                      onPressed: onManageMaintenance,
                    ),
                    IconButton(
                      tooltip: 'Edit',
                      icon: const Icon(Icons.edit_outlined),
                      color: AppColors.textSecondary,
                      onPressed: onEdit,
                    ),
                    IconButton(
                      tooltip: 'Delete',
                      icon: const Icon(Icons.delete_outline),
                      color: AppColors.danger,
                      onPressed: onDelete,
                    ),
                  ],
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  /// Surface + indoor/outdoor + city, all names — never ids.
  String get _subtitle {
    final placement = court.isIndoor ? 'Indoor' : 'Outdoor';
    final parts = <String>[
      court.surfaceTypeName,
      placement,
      if (court.cityName.isNotEmpty) court.cityName,
    ].where((p) => p.isNotEmpty);
    return parts.join(' · ');
  }
}

/// The card's header image: the court's primary image, or a muted placeholder
/// icon when no URL is present / it fails to load. Constrained to a fixed banner
/// so the image never exceeds the card body (rubric — image not dominating).
///
/// [imageUrl] arrives RELATIVE (`/api/images/{id}`); the absolute URL is composed
/// here from the configured base URL ([appConfigProvider]) so no host is ever
/// hardcoded.
class _CourtImage extends ConsumerWidget {
  const _CourtImage({this.imageUrl});

  final String? imageUrl;

  static const double _height = 140;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final relative = imageUrl;
    final url = (relative == null || relative.isEmpty)
        ? ''
        : absoluteImageUrl(ref.watch(appConfigProvider).apiBaseUrl, relative);
    final hasImage = url.isNotEmpty;

    return SizedBox(
      height: _height,
      width: double.infinity,
      child: hasImage
          ? CachedNetworkImage(
              imageUrl: url,
              fit: BoxFit.cover,
              placeholder: (context, _) => const ColoredBox(
                color: AppColors.surfaceMuted,
                child: Center(
                  child: SizedBox(
                    width: AppSpacing.lg,
                    height: AppSpacing.lg,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  ),
                ),
              ),
              errorWidget: (context, _, __) => const _ImageFallback(),
            )
          : const _ImageFallback(),
    );
  }
}

class _ImageFallback extends StatelessWidget {
  const _ImageFallback();

  @override
  Widget build(BuildContext context) {
    return const ColoredBox(
      color: AppColors.surfaceMuted,
      child: Center(
        child: Icon(
          Icons.sports_tennis_outlined,
          color: AppColors.textMuted,
          size: AppSpacing.xl,
        ),
      ),
    );
  }
}
