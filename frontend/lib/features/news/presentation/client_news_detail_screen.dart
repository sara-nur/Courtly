import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/env/app_config.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../../../core/utils/formatters.dart';
import '../../../core/utils/image_urls.dart';
import '../domain/news_models.dart';

/// Full-article reader for a published [News] item (F23). Reached by tapping a
/// news card on the Home feed; the article is passed in directly (the published
/// feed already carries the full text), so no extra fetch is needed. Renders the
/// hero image, title, published date + author, and the full body — with the
/// standard app-bar back button (rubric §6). Lives on its own route above the
/// bottom-nav shell, so it is a full screen rather than a tab.
class ClientNewsDetailScreen extends ConsumerWidget {
  const ClientNewsDetailScreen({super.key, required this.news});

  /// Nullable so a stray navigation without an article (e.g. a cold deep link)
  /// degrades to a friendly message instead of crashing.
  final News? news;

  /// Hero banner height for the article image.
  static const double _heroHeight = 220;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final article = news;

    return Scaffold(
      appBar: AppBar(title: const Text('News')),
      body: article == null
          ? const _Unavailable()
          : ListView(
              padding: EdgeInsets.zero,
              children: [
                _Hero(imageUrl: article.imageUrl, height: _heroHeight),
                Padding(
                  padding: const EdgeInsets.all(AppSpacing.lg),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        article.title,
                        style: theme.textTheme.headlineSmall
                            ?.copyWith(fontWeight: FontWeight.w800),
                      ),
                      const SizedBox(height: AppSpacing.xs),
                      _MetaRow(
                        publishedAtUtc: article.publishedAtUtc,
                        author: article.authorName,
                      ),
                      const SizedBox(height: AppSpacing.md),
                      Text(
                        article.text,
                        style: theme.textTheme.bodyLarge?.copyWith(
                          color: AppColors.textSecondary,
                          height: 1.5,
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ),
    );
  }
}

/// The article's leading image, or a muted placeholder when none is present /
/// it fails to load. [imageUrl] arrives RELATIVE (`/api/news/{id}/image`); the
/// absolute URL is composed from the configured base URL so no host is hardcoded.
class _Hero extends ConsumerWidget {
  const _Hero({this.imageUrl, required this.height});

  final String? imageUrl;
  final double height;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final relative = imageUrl;
    final url = (relative == null || relative.isEmpty)
        ? ''
        : absoluteImageUrl(ref.watch(appConfigProvider).apiBaseUrl, relative);

    return SizedBox(
      height: height,
      width: double.infinity,
      child: url.isEmpty
          ? const _HeroFallback()
          : CachedNetworkImage(
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
              errorWidget: (context, _, __) => const _HeroFallback(),
            ),
    );
  }
}

class _HeroFallback extends StatelessWidget {
  const _HeroFallback();

  @override
  Widget build(BuildContext context) {
    return const ColoredBox(
      color: AppColors.surfaceMuted,
      child: Center(
        child: Icon(
          Icons.article_outlined,
          color: AppColors.textMuted,
          size: AppSpacing.xxl,
        ),
      ),
    );
  }
}

/// "Published date · author" byline under the title.
class _MetaRow extends StatelessWidget {
  const _MetaRow({required this.publishedAtUtc, required this.author});

  final DateTime publishedAtUtc;
  final String author;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final parts = <String>[
      Formatters.date(publishedAtUtc.toLocal()),
      if (author.isNotEmpty) author,
    ];
    return Text(
      parts.join('  ·  '),
      style: theme.textTheme.bodySmall?.copyWith(color: AppColors.textMuted),
    );
  }
}

/// Shown when the screen is reached without an article to render.
class _Unavailable extends StatelessWidget {
  const _Unavailable();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(AppSpacing.xl),
        child: Text(
          'This article is unavailable.',
          textAlign: TextAlign.center,
          style: theme.textTheme.titleMedium
              ?.copyWith(color: AppColors.textSecondary),
        ),
      ),
    );
  }
}
