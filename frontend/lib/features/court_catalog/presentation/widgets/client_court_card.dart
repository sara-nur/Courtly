import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/env/app_config.dart';
import '../../../../core/theme/app_colors.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../core/utils/image_urls.dart';
import '../../domain/court_models.dart';

/// Client card for one [Court], matching the mobile mockup: the court image
/// fills the top, a rating badge floats top-right (only when the court has a
/// rating), a surface-type chip floats over the image, and the court name +
/// hourly price sit below.
///
/// Tapping opens the court detail screen (F24) when [onTap] is supplied; the
/// card stays navigation-agnostic (the caller wires the route). [width] is
/// optional so the same card works both in a fixed-width horizontal carousel and
/// as a full-width list item (null → it fills its parent).
class ClientCourtCard extends ConsumerWidget {
  const ClientCourtCard({super.key, required this.court, this.width, this.onTap});

  final Court court;
  final double? width;

  /// Opens the court detail when tapped; null leaves the card non-interactive.
  final VoidCallback? onTap;

  /// Banner height for the top image.
  static const double _imageHeight = 150;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);

    return SizedBox(
      width: width,
      child: Material(
        color: AppColors.surface,
        clipBehavior: Clip.antiAlias,
        borderRadius: AppSpacing.brLg,
        child: InkWell(
          onTap: onTap,
          child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          mainAxisSize: MainAxisSize.min,
          children: [
            Stack(
              children: [
                _CourtImage(
                  imageUrl: court.primaryImageUrl,
                  height: _imageHeight,
                ),
                if (court.avgRating != null)
                  Positioned(
                    top: AppSpacing.xs,
                    right: AppSpacing.xs,
                    child: _RatingBadge(rating: court.avgRating!),
                  ),
                Positioned(
                  left: AppSpacing.xs,
                  bottom: AppSpacing.xs,
                  child: _SurfaceChip(label: court.surfaceTypeName),
                ),
              ],
            ),
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
                        ?.copyWith(fontWeight: FontWeight.w700),
                  ),
                  const SizedBox(height: AppSpacing.xxs),
                  Text(
                    _priceLabel,
                    style: theme.textTheme.titleSmall
                        ?.copyWith(color: AppColors.primary),
                  ),
                ],
              ),
            ),
          ],
          ),
        ),
      ),
    );
  }

  /// `$` + hourly price (no decimals when whole) + per-hour suffix.
  String get _priceLabel {
    final price = court.hourlyPrice;
    final whole = price == price.roundToDouble();
    final amount = whole ? price.toStringAsFixed(0) : price.toStringAsFixed(2);
    return '\$$amount / hour';
  }
}

/// The card's header image: the court's primary image, or a muted placeholder
/// icon when no URL is present / it fails to load.
///
/// [imageUrl] arrives RELATIVE (`/api/images/{id}`); the absolute URL is composed
/// here from the configured base URL ([appConfigProvider]) so no host is ever
/// hardcoded.
class _CourtImage extends ConsumerWidget {
  const _CourtImage({this.imageUrl, required this.height});

  final String? imageUrl;
  final double height;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final relative = imageUrl;
    final url = (relative == null || relative.isEmpty)
        ? ''
        : absoluteImageUrl(ref.watch(appConfigProvider).apiBaseUrl, relative);
    final hasImage = url.isNotEmpty;

    return SizedBox(
      height: height,
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

/// A small star + rating pill floated over the top-right of the image.
class _RatingBadge extends StatelessWidget {
  const _RatingBadge({required this.rating});

  final double rating;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.xs,
        vertical: AppSpacing.xxs,
      ),
      decoration: const BoxDecoration(
        color: AppColors.surface,
        borderRadius: AppSpacing.brPill,
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          const Icon(Icons.star, size: AppSpacing.md, color: AppColors.warning),
          const SizedBox(width: AppSpacing.xxs),
          Text(
            rating.toStringAsFixed(1),
            style: Theme.of(context).textTheme.labelMedium?.copyWith(
                  color: AppColors.textPrimary,
                  fontWeight: FontWeight.w600,
                ),
          ),
        ],
      ),
    );
  }
}

/// The surface-type label chip floated over the bottom-left of the image.
class _SurfaceChip extends StatelessWidget {
  const _SurfaceChip({required this.label});

  final String label;

  @override
  Widget build(BuildContext context) {
    if (label.isEmpty) return const SizedBox.shrink();
    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.xs,
        vertical: AppSpacing.xxs,
      ),
      decoration: const BoxDecoration(
        color: AppColors.surface,
        borderRadius: AppSpacing.brPill,
      ),
      child: Text(
        label,
        style: Theme.of(context).textTheme.labelMedium?.copyWith(
              color: AppColors.textSecondary,
              fontWeight: FontWeight.w600,
            ),
      ),
    );
  }
}
