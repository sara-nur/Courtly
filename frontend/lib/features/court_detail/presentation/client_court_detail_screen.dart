import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../app/router/client_router.dart';
import '../../../core/env/app_config.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../../../core/utils/formatters.dart';
import '../../../core/utils/image_urls.dart';
import '../../../core/widgets/disabled_action.dart';
import '../../../core/widgets/star_rating.dart';
import '../../court_catalog/domain/court_models.dart';
import '../../reviews/application/review_providers.dart';
import '../../reviews/domain/review_models.dart';
import '../application/court_detail_providers.dart';
import 'widgets/review_tile.dart';
import 'widgets/write_review_sheet.dart';

/// Client Court Details screen (F24, mockup p.8 §3.2.2). Master-detail #2: the
/// court (hero photo, name, price, location, rating, surface/indoor/amenity
/// chips, "About this court") with its reviews as the child list, plus a sticky
/// "Total price + Book Now" bar. Reached by tapping a court card on Home/Search.
///
/// All data loads together via [courtDetailProvider]; the loading/error states
/// keep their own app bar so the back button is always available. Book Now is
/// intentionally disabled until the booking flow (F25) exists, and the
/// "Write a review" entry appears only when the user has a completed,
/// not-yet-reviewed booking for this court.
class ClientCourtDetailScreen extends ConsumerWidget {
  const ClientCourtDetailScreen({super.key, required this.courtId});

  final int courtId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final value = ref.watch(courtDetailProvider(courtId));

    return value.when(
      loading: () => const Scaffold(
        body: Center(child: CircularProgressIndicator()),
      ),
      error: (e, _) => Scaffold(
        appBar: AppBar(),
        body: _ErrorState(
          onRetry: () => ref.invalidate(courtDetailProvider(courtId)),
        ),
      ),
      data: (data) => _CourtDetailView(courtId: courtId, data: data),
    );
  }
}

class _CourtDetailView extends ConsumerWidget {
  const _CourtDetailView({required this.courtId, required this.data});

  final int courtId;
  final CourtDetailData data;

  static const double _heroHeight = 280;

  Future<void> _openWriteReview(BuildContext context, WidgetRef ref) async {
    final reservationId = data.eligibility.reservationId;
    if (reservationId == null) return;

    final result = await showModalBottomSheet<Review>(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.transparent,
      builder: (_) => WriteReviewSheet(
        reservationId: reservationId,
        courtName: data.court.name,
      ),
    );

    if (result != null) {
      // Refresh the detail (rating/count + preview + eligibility now false) and
      // any open "See all" pages.
      ref.invalidate(courtDetailProvider(courtId));
      ref.invalidate(courtReviewsPageProvider);
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Thanks for your review!')),
        );
      }
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final court = data.court;

    return Scaffold(
      body: CustomScrollView(
        slivers: [
          SliverAppBar(
            pinned: true,
            expandedHeight: _heroHeight,
            foregroundColor: AppColors.onPrimary,
            backgroundColor: AppColors.primaryDark,
            flexibleSpace: FlexibleSpaceBar(
              background: _HeroImage(imageUrl: court.primaryImageUrl),
            ),
          ),
          SliverToBoxAdapter(
            child: Padding(
              padding: AppSpacing.pagePadding,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  _TitleRow(court: court),
                  const SizedBox(height: AppSpacing.xs),
                  _LocationRow(
                    cityName: court.cityName,
                    countryName: court.countryName,
                  ),
                  const SizedBox(height: AppSpacing.sm),
                  _RatingRow(
                    avgRating: court.avgRating,
                    reviewCount: court.reviewCount,
                    onSeeAll: court.reviewCount > 0
                        ? () => context.push(
                              ClientRoutes.courtReviewsPath(courtId),
                              extra: court.name,
                            )
                        : null,
                  ),
                  const SizedBox(height: AppSpacing.md),
                  _ChipsWrap(court: court, amenities: data.amenities),
                  const SizedBox(height: AppSpacing.lg),
                  _About(description: court.description),
                  const SizedBox(height: AppSpacing.lg),
                  _ReviewsSection(
                    reviews: data.reviews.items,
                    totalCount: data.reviews.totalCount,
                    canReview: data.eligibility.canReview,
                    onWriteReview: () => _openWriteReview(context, ref),
                    onSeeAll: () => context.push(
                      ClientRoutes.courtReviewsPath(courtId),
                      extra: court.name,
                    ),
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
      bottomNavigationBar: _BookingBar(hourlyPrice: court.hourlyPrice),
    );
  }
}

/// The collapsing hero photo, or a muted placeholder when none / it fails. The
/// relative `/api/images/{id}` URL is composed from the configured base URL.
class _HeroImage extends ConsumerWidget {
  const _HeroImage({this.imageUrl});

  final String? imageUrl;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final relative = imageUrl;
    final url = (relative == null || relative.isEmpty)
        ? ''
        : absoluteImageUrl(ref.watch(appConfigProvider).apiBaseUrl, relative);

    if (url.isEmpty) return const _HeroFallback();
    return CachedNetworkImage(
      imageUrl: url,
      fit: BoxFit.cover,
      placeholder: (context, _) => const ColoredBox(color: AppColors.surfaceMuted),
      errorWidget: (context, _, __) => const _HeroFallback(),
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
          Icons.sports_tennis_outlined,
          color: AppColors.textMuted,
          size: AppSpacing.xxl,
        ),
      ),
    );
  }
}

/// Court name (left) + price-per-hour (right).
class _TitleRow extends StatelessWidget {
  const _TitleRow({required this.court});

  final Court court;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Expanded(
          child: Text(
            court.name,
            style: theme.textTheme.headlineSmall
                ?.copyWith(fontWeight: FontWeight.w800),
          ),
        ),
        const SizedBox(width: AppSpacing.sm),
        Column(
          crossAxisAlignment: CrossAxisAlignment.end,
          children: [
            Text(
              Formatters.money(court.hourlyPrice),
              style: theme.textTheme.titleLarge
                  ?.copyWith(color: AppColors.primary, fontWeight: FontWeight.w800),
            ),
            Text(
              'per hour',
              style: theme.textTheme.bodySmall
                  ?.copyWith(color: AppColors.textMuted),
            ),
          ],
        ),
      ],
    );
  }
}

class _LocationRow extends StatelessWidget {
  const _LocationRow({required this.cityName, required this.countryName});

  final String cityName;
  final String countryName;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final parts = [cityName, countryName].where((p) => p.isNotEmpty).toList();
    if (parts.isEmpty) return const SizedBox.shrink();
    return Row(
      children: [
        const Icon(Icons.place_outlined,
            size: AppSpacing.md, color: AppColors.textMuted),
        const SizedBox(width: AppSpacing.xxs),
        Expanded(
          child: Text(
            parts.join(', '),
            style: theme.textTheme.bodyMedium
                ?.copyWith(color: AppColors.textSecondary),
          ),
        ),
      ],
    );
  }
}

/// Star average + "(N reviews)" + optional "See all".
class _RatingRow extends StatelessWidget {
  const _RatingRow({
    required this.avgRating,
    required this.reviewCount,
    this.onSeeAll,
  });

  final double? avgRating;
  final int reviewCount;
  final VoidCallback? onSeeAll;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final avg = avgRating;

    if (avg == null) {
      return Text(
        'No reviews yet',
        style: theme.textTheme.bodyMedium?.copyWith(color: AppColors.textMuted),
      );
    }

    final label = reviewCount == 1 ? '1 review' : '$reviewCount reviews';
    return Row(
      children: [
        StarRating(rating: avg, size: AppSpacing.md),
        const SizedBox(width: AppSpacing.xs),
        Text(
          '${avg.toStringAsFixed(1)} ($label)',
          style: theme.textTheme.bodyMedium
              ?.copyWith(color: AppColors.textSecondary),
        ),
        const Spacer(),
        if (onSeeAll != null)
          TextButton(onPressed: onSeeAll, child: const Text('See all')),
      ],
    );
  }
}

/// Surface / indoor-outdoor / amenity chips.
class _ChipsWrap extends StatelessWidget {
  const _ChipsWrap({required this.court, required this.amenities});

  final Court court;
  final List<CourtAmenityLink> amenities;

  @override
  Widget build(BuildContext context) {
    return Wrap(
      spacing: AppSpacing.xs,
      runSpacing: AppSpacing.xs,
      children: [
        if (court.surfaceTypeName.isNotEmpty)
          _InfoChip(label: court.surfaceTypeName.toUpperCase(), highlighted: true),
        _InfoChip(label: court.isIndoor ? 'Indoor' : 'Outdoor'),
        for (final a in amenities) _InfoChip(label: a.amenityName),
      ],
    );
  }
}

class _InfoChip extends StatelessWidget {
  const _InfoChip({required this.label, this.highlighted = false});

  final String label;
  final bool highlighted;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.sm,
        vertical: AppSpacing.xs,
      ),
      decoration: BoxDecoration(
        color: highlighted ? AppColors.primarySoft : AppColors.surfaceMuted,
        borderRadius: AppSpacing.brPill,
      ),
      child: Text(
        label,
        style: theme.textTheme.labelMedium?.copyWith(
          color: highlighted ? AppColors.primary : AppColors.textSecondary,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }
}

class _About extends StatelessWidget {
  const _About({required this.description});

  final String? description;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final text = description?.trim();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          'About this court',
          style:
              theme.textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w700),
        ),
        const SizedBox(height: AppSpacing.xs),
        Text(
          (text == null || text.isEmpty) ? 'No description provided.' : text,
          style: theme.textTheme.bodyMedium
              ?.copyWith(color: AppColors.textSecondary, height: 1.5),
        ),
      ],
    );
  }
}

/// "Reviews" header (+ "Write a review" when eligible), an inline preview of the
/// latest reviews, and a "See all N reviews" link when there are more.
class _ReviewsSection extends StatelessWidget {
  const _ReviewsSection({
    required this.reviews,
    required this.totalCount,
    required this.canReview,
    required this.onWriteReview,
    required this.onSeeAll,
  });

  final List<Review> reviews;
  final int totalCount;
  final bool canReview;
  final VoidCallback onWriteReview;
  final VoidCallback onSeeAll;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Expanded(
              child: Text(
                'Reviews',
                style: theme.textTheme.titleMedium
                    ?.copyWith(fontWeight: FontWeight.w700),
              ),
            ),
            if (canReview)
              TextButton.icon(
                onPressed: onWriteReview,
                icon: const Icon(Icons.rate_review_outlined, size: AppSpacing.md),
                label: const Text('Write a review'),
              ),
          ],
        ),
        const SizedBox(height: AppSpacing.xs),
        if (reviews.isEmpty)
          Text(
            'No reviews yet. Be the first to review this court.',
            style:
                theme.textTheme.bodyMedium?.copyWith(color: AppColors.textMuted),
          )
        else
          Column(
            children: [
              for (var i = 0; i < reviews.length; i++) ...[
                if (i > 0) const Divider(height: AppSpacing.lg),
                ReviewTile(review: reviews[i]),
              ],
            ],
          ),
        if (totalCount > reviews.length) ...[
          const SizedBox(height: AppSpacing.xs),
          Align(
            alignment: Alignment.centerLeft,
            child: TextButton(
              onPressed: onSeeAll,
              child: Text('See all $totalCount reviews'),
            ),
          ),
        ],
      ],
    );
  }
}

/// The sticky bottom bar: total price (one hour) + a disabled "Book Now" (the
/// booking flow lands in F25, so the control is present but not yet wired).
class _BookingBar extends StatelessWidget {
  const _BookingBar({required this.hourlyPrice});

  final double hourlyPrice;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Material(
      color: AppColors.surface,
      elevation: 8,
      child: SafeArea(
        top: false,
        child: Padding(
          padding: const EdgeInsets.symmetric(
            horizontal: AppSpacing.lg,
            vertical: AppSpacing.md,
          ),
          child: Row(
            children: [
              Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(
                    'Total price',
                    style: theme.textTheme.bodySmall
                        ?.copyWith(color: AppColors.textMuted),
                  ),
                  Text(
                    Formatters.money(hourlyPrice),
                    style: theme.textTheme.titleLarge
                        ?.copyWith(fontWeight: FontWeight.w800),
                  ),
                ],
              ),
              const Spacer(),
              DisabledAction(
                enabled: false,
                reason: 'Booking opens in a later update',
                child: FilledButton.icon(
                  onPressed: () {},
                  icon: const Icon(Icons.arrow_forward),
                  label: const Text('Book Now'),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _ErrorState extends StatelessWidget {
  const _ErrorState({required this.onRetry});

  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          const Icon(Icons.error_outline, color: AppColors.danger),
          const SizedBox(height: AppSpacing.xs),
          Text(
            'Could not load this court',
            style: theme.textTheme.bodyMedium
                ?.copyWith(color: AppColors.textSecondary),
          ),
          const SizedBox(height: AppSpacing.xs),
          TextButton(onPressed: onRetry, child: const Text('Retry')),
        ],
      ),
    );
  }
}
