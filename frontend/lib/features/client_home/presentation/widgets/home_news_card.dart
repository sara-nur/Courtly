import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/env/app_config.dart';
import '../../../../core/theme/app_colors.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../core/utils/image_urls.dart';
import '../../../news/domain/news_models.dart';

/// A compact, display-only news card for the Home feed (F23): a leading image,
/// the title and a short text snippet. The image arrives RELATIVE
/// (`/api/news/{id}/image`); the absolute URL is composed here from the
/// configured base URL ([appConfigProvider]) so no host is ever hardcoded.
class HomeNewsCard extends ConsumerWidget {
  const HomeNewsCard({super.key, required this.news, this.onTap});

  final News news;

  /// Tap handler — opens the full article. Null makes the card non-interactive.
  final VoidCallback? onTap;

  /// Square thumbnail edge for the leading image.
  static const double _thumbSize = 72;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);

    return Material(
      color: AppColors.surface,
      clipBehavior: Clip.antiAlias,
      borderRadius: AppSpacing.brLg,
      child: InkWell(
        onTap: onTap,
        // IntrinsicHeight gives the row a finite height from its children's
        // natural sizes, so `stretch` can size the thumbnail to the text height
        // without an unbounded-height Row (which forces infinite-height children).
        child: IntrinsicHeight(
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              _NewsThumb(imageUrl: news.imageUrl, size: _thumbSize),
              Expanded(
                child: Padding(
                  padding: AppSpacing.cardPadding,
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    mainAxisAlignment: MainAxisAlignment.center,
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Text(
                        news.title,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: theme.textTheme.titleSmall
                            ?.copyWith(fontWeight: FontWeight.w700),
                      ),
                      const SizedBox(height: AppSpacing.xxs),
                      Text(
                        news.text,
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                        style: theme.textTheme.bodySmall
                            ?.copyWith(color: AppColors.textSecondary),
                      ),
                    ],
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// The card's leading thumbnail: the news image, or a muted placeholder icon
/// when no URL is present / it fails to load.
class _NewsThumb extends ConsumerWidget {
  const _NewsThumb({this.imageUrl, required this.size});

  final String? imageUrl;
  final double size;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final relative = imageUrl;
    final url = (relative == null || relative.isEmpty)
        ? ''
        : absoluteImageUrl(ref.watch(appConfigProvider).apiBaseUrl, relative);

    return SizedBox(
      width: size,
      height: size,
      child: url.isEmpty
          ? const _ThumbFallback()
          : CachedNetworkImage(
              imageUrl: url,
              fit: BoxFit.cover,
              placeholder: (context, _) => const ColoredBox(
                color: AppColors.surfaceMuted,
                child: Center(
                  child: SizedBox(
                    width: AppSpacing.md,
                    height: AppSpacing.md,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  ),
                ),
              ),
              errorWidget: (context, _, __) => const _ThumbFallback(),
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
      child: Center(
        child: Icon(
          Icons.article_outlined,
          color: AppColors.textMuted,
          size: AppSpacing.lg,
        ),
      ),
    );
  }
}
