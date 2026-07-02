import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/env/app_config.dart';
import '../../../../core/theme/app_colors.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../core/utils/image_urls.dart';
import '../../domain/dashboard_models.dart';
import 'chart_placeholder.dart';
import 'dashboard_card.dart';

/// Most Popular Courts (PRD p.5): the top courts by booking volume as horizontal
/// percentage bars, each labelled by court name + image (never a raw id).
class PopularCourtsChart extends StatelessWidget {
  const PopularCourtsChart({super.key, required this.courts});

  final List<PopularCourt> courts;

  @override
  Widget build(BuildContext context) {
    return DashboardCard(
      title: 'Most Popular Courts',
      subtitle: 'Share of bookings in the selected period',
      child: courts.isEmpty
          ? const SizedBox(height: 120, child: ChartPlaceholder(message: 'No bookings in this period'))
          : Column(
              children: [
                for (var i = 0; i < courts.length; i++) ...[
                  if (i > 0) const SizedBox(height: AppSpacing.md),
                  _PopularCourtRow(court: courts[i]),
                ],
              ],
            ),
    );
  }
}

class _PopularCourtRow extends ConsumerWidget {
  const _PopularCourtRow({required this.court});

  final PopularCourt court;

  static const double _barHeight = 8;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final widthFactor = (court.percentage / 100).clamp(0.0, 1.0).toDouble();

    return Row(
      crossAxisAlignment: CrossAxisAlignment.center,
      children: [
        _Thumbnail(imageUrl: court.imageUrl),
        const SizedBox(width: AppSpacing.sm),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Text(
                      court.courtName,
                      style: theme.textTheme.bodyMedium,
                      overflow: TextOverflow.ellipsis,
                    ),
                  ),
                  const SizedBox(width: AppSpacing.xs),
                  Text(
                    '${court.percentage.toStringAsFixed(1)}%',
                    style: theme.textTheme.bodyMedium?.copyWith(fontWeight: FontWeight.w600),
                  ),
                ],
              ),
              const SizedBox(height: AppSpacing.xxs),
              ClipRRect(
                borderRadius: AppSpacing.brPill,
                child: Container(
                  height: _barHeight,
                  color: AppColors.surfaceMuted,
                  child: FractionallySizedBox(
                    alignment: Alignment.centerLeft,
                    widthFactor: widthFactor,
                    child: Container(color: AppColors.primary),
                  ),
                ),
              ),
              const SizedBox(height: AppSpacing.xxs),
              Text(
                '${court.count} ${court.count == 1 ? 'booking' : 'bookings'}',
                style: theme.textTheme.bodySmall?.copyWith(color: AppColors.textMuted),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

/// The court's primary image (relative `/api/images/{id}`, absolutized here), or
/// a muted placeholder icon when absent / it fails to load.
class _Thumbnail extends ConsumerWidget {
  const _Thumbnail({this.imageUrl});

  final String? imageUrl;

  static const double _size = 40;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final relative = imageUrl;
    final url = (relative == null || relative.isEmpty)
        ? ''
        : absoluteImageUrl(ref.watch(appConfigProvider).apiBaseUrl, relative);

    return ClipRRect(
      borderRadius: AppSpacing.brSm,
      child: SizedBox(
        width: _size,
        height: _size,
        child: url.isEmpty
            ? const _ThumbFallback()
            : CachedNetworkImage(
                imageUrl: url,
                fit: BoxFit.cover,
                placeholder: (context, _) => const ColoredBox(color: AppColors.surfaceMuted),
                errorWidget: (context, _, __) => const _ThumbFallback(),
              ),
      ),
    );
  }
}

class _ThumbFallback extends StatelessWidget {
  const _ThumbFallback();

  @override
  Widget build(BuildContext context) {
    return const ColoredBox(
      color: AppColors.surfaceMuted,
      child: Icon(Icons.sports_tennis_outlined, size: 20, color: AppColors.textMuted),
    );
  }
}
