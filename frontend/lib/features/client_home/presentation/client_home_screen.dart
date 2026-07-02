import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../app/router/client_router.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../../../core/widgets/async_value_view.dart';
import '../../court_catalog/domain/court_models.dart';
import '../../court_catalog/presentation/widgets/client_court_card.dart';
import '../../news/domain/news_models.dart';
import '../../reservations/domain/reservation_models.dart';
import '../../reservations/presentation/widgets/booking_summary_tile.dart';
import '../application/home_controller.dart';
import 'widgets/home_news_card.dart';

/// Client Home tab (F23, mockup p.8): a "Ready to serve?" hero with a tappable
/// search entry + filter chips, a featured-courts carousel, the customer's
/// recent bookings, and the latest news. All data comes from [homeDataProvider]
/// (the three sections loaded in parallel) and renders via [AsyncValueView].
class ClientHomeScreen extends ConsumerWidget {
  const ClientHomeScreen({super.key});

  /// Height of the featured-courts horizontal carousel. Fits the card's 150px
  /// image plus its name/price block (~81px) with a little slack so the card
  /// never overflows the bounded carousel height.
  static const double _carouselHeight = 244;

  /// Fixed width of each featured court card in the carousel.
  static const double _courtCardWidth = 180;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final value = ref.watch(homeDataProvider);

    return AsyncValueView<HomeData>(
      value: value,
      onRetry: () => ref.invalidate(homeDataProvider),
      data: (data) => ListView(
        padding: const EdgeInsets.symmetric(vertical: AppSpacing.lg),
        children: [
          const Padding(
            padding: EdgeInsets.symmetric(horizontal: AppSpacing.lg),
            child: _Hero(),
          ),
          const SizedBox(height: AppSpacing.lg),
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: AppSpacing.lg),
            child: _SearchBar(onTap: () => context.go(ClientRoutes.search)),
          ),
          const SizedBox(height: AppSpacing.sm),
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: AppSpacing.lg),
            child: _FilterChips(onTap: () => context.go(ClientRoutes.search)),
          ),
          const SizedBox(height: AppSpacing.lg),
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: AppSpacing.lg),
            child: _RecommendedForYouBanner(
              onTap: () => context.push(ClientRoutes.recommendations),
            ),
          ),
          const SizedBox(height: AppSpacing.xl),
          _FeaturedCourtsSection(
            courts: data.featuredCourts,
            carouselHeight: _carouselHeight,
            cardWidth: _courtCardWidth,
            onSeeAll: () => context.go(ClientRoutes.search),
          ),
          const SizedBox(height: AppSpacing.xl),
          _RecentBookingsSection(bookings: data.recentBookings),
          const SizedBox(height: AppSpacing.xl),
          _NewsSection(news: data.news),
          const SizedBox(height: AppSpacing.lg),
        ],
      ),
    );
  }
}

/// The "Ready to serve?" hero heading.
class _Hero extends StatelessWidget {
  const _Hero();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Text(
      'Ready to serve?',
      style: theme.textTheme.headlineMedium?.copyWith(
        fontWeight: FontWeight.w800,
        color: AppColors.textPrimary,
      ),
    );
  }
}

/// The "Recommended For You" entry — a prominent, on-brand banner that opens the
/// full recommendations screen (F29). It is the Home surfacing of the app's smart
/// recommender (mockup p.10): a single tappable CTA rather than a new bottom-nav
/// tab, so the existing five-tab shell is unchanged.
class _RecommendedForYouBanner extends StatelessWidget {
  const _RecommendedForYouBanner({required this.onTap});

  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Material(
      color: AppColors.primary,
      borderRadius: AppSpacing.brLg,
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: onTap,
        child: Padding(
          padding: AppSpacing.cardPadding,
          child: Row(
            children: [
              const Icon(Icons.auto_awesome, color: AppColors.onPrimary),
              const SizedBox(width: AppSpacing.sm),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Text(
                      'Recommended For You',
                      style: theme.textTheme.titleMedium?.copyWith(
                        color: AppColors.onPrimary,
                        fontWeight: FontWeight.w800,
                      ),
                    ),
                    const SizedBox(height: AppSpacing.xxs),
                    Text(
                      'Smart picks based on how you play',
                      style: theme.textTheme.bodySmall?.copyWith(color: Colors.white70),
                    ),
                  ],
                ),
              ),
              const Icon(Icons.arrow_forward, color: AppColors.onPrimary),
            ],
          ),
        ),
      ),
    );
  }
}

/// A tappable, read-only search field that routes to the Search tab. Styled to
/// look like an input (hint + trailing tune icon) but it is just a button.
class _SearchBar extends StatelessWidget {
  const _SearchBar({required this.onTap});

  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Material(
      color: AppColors.surfaceMuted,
      borderRadius: AppSpacing.brPill,
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.symmetric(
            horizontal: AppSpacing.md,
            vertical: AppSpacing.sm,
          ),
          child: Row(
            children: [
              const Icon(Icons.search, color: AppColors.textMuted),
              const SizedBox(width: AppSpacing.sm),
              Expanded(
                child: Text(
                  'Find a court near you...',
                  style: theme.textTheme.bodyMedium
                      ?.copyWith(color: AppColors.textMuted),
                ),
              ),
              const SizedBox(width: AppSpacing.sm),
              const Icon(Icons.tune, color: AppColors.primary),
            ],
          ),
        ),
      ),
    );
  }
}

/// The quick-filter chip row ("Price" / "Surface" / "Rating"). Each chip routes
/// to the Search tab where the real filters live.
class _FilterChips extends StatelessWidget {
  const _FilterChips({required this.onTap});

  final VoidCallback onTap;

  static const List<String> _labels = ['Price', 'Surface', 'Rating'];

  @override
  Widget build(BuildContext context) {
    return Wrap(
      spacing: AppSpacing.xs,
      runSpacing: AppSpacing.xs,
      children: [
        for (final label in _labels)
          ActionChip(
            label: Text(label),
            onPressed: onTap,
          ),
      ],
    );
  }
}

/// A section header: a bold title with an optional trailing "See all" action.
class _SectionHeader extends StatelessWidget {
  const _SectionHeader({required this.title, this.onSeeAll});

  final String title;
  final VoidCallback? onSeeAll;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: AppSpacing.lg),
      child: Row(
        children: [
          Expanded(
            child: Text(
              title,
              style: theme.textTheme.titleLarge
                  ?.copyWith(fontWeight: FontWeight.w700),
            ),
          ),
          if (onSeeAll != null)
            TextButton(
              onPressed: onSeeAll,
              child: const Text('See all'),
            ),
        ],
      ),
    );
  }
}

/// Featured courts header + horizontal carousel of [ClientCourtCard].
class _FeaturedCourtsSection extends StatelessWidget {
  const _FeaturedCourtsSection({
    required this.courts,
    required this.carouselHeight,
    required this.cardWidth,
    required this.onSeeAll,
  });

  final List<Court> courts;
  final double carouselHeight;
  final double cardWidth;
  final VoidCallback onSeeAll;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _SectionHeader(title: 'Featured Courts', onSeeAll: onSeeAll),
        const SizedBox(height: AppSpacing.sm),
        if (courts.isEmpty)
          const Padding(
            padding: EdgeInsets.symmetric(horizontal: AppSpacing.lg),
            child: _EmptyHint(message: 'No featured courts yet'),
          )
        else
          SizedBox(
            height: carouselHeight,
            child: ListView.separated(
              scrollDirection: Axis.horizontal,
              padding: const EdgeInsets.symmetric(horizontal: AppSpacing.lg),
              itemCount: courts.length,
              separatorBuilder: (_, __) =>
                  const SizedBox(width: AppSpacing.sm),
              itemBuilder: (context, index) => ClientCourtCard(
                court: courts[index],
                width: cardWidth,
                onTap: () => context.push(
                  ClientRoutes.courtDetailPath(courts[index].id),
                ),
              ),
            ),
          ),
      ],
    );
  }
}

/// Recent bookings header + display-only tiles (or an empty hint).
class _RecentBookingsSection extends StatelessWidget {
  const _RecentBookingsSection({required this.bookings});

  final List<Reservation> bookings;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _SectionHeader(
          title: 'Recent Bookings',
          onSeeAll: () => context.go(ClientRoutes.bookings),
        ),
        const SizedBox(height: AppSpacing.sm),
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: AppSpacing.lg),
          child: bookings.isEmpty
              ? const _EmptyHint(message: 'No bookings yet')
              : Column(
                  children: [
                    for (var i = 0; i < bookings.length; i++) ...[
                      if (i > 0) const SizedBox(height: AppSpacing.sm),
                      BookingSummaryTile(
                        reservation: bookings[i],
                        onTap: () => context.push(
                          ClientRoutes.bookingDetailPath(bookings[i].id),
                        ),
                      ),
                    ],
                  ],
                ),
        ),
      ],
    );
  }
}

/// News header + the latest published articles as compact cards.
class _NewsSection extends StatelessWidget {
  const _NewsSection({required this.news});

  final List<News> news;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const _SectionHeader(title: 'News'),
        const SizedBox(height: AppSpacing.sm),
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: AppSpacing.lg),
          child: news.isEmpty
              ? const _EmptyHint(message: 'No news yet')
              : Column(
                  children: [
                    for (var i = 0; i < news.length; i++) ...[
                      if (i > 0) const SizedBox(height: AppSpacing.sm),
                      HomeNewsCard(
                        news: news[i],
                        onTap: () => context.push(
                          ClientRoutes.newsDetail,
                          extra: news[i],
                        ),
                      ),
                    ],
                  ],
                ),
        ),
      ],
    );
  }
}

/// A small muted placeholder shown when a section has no items.
class _EmptyHint extends StatelessWidget {
  const _EmptyHint({required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Container(
      width: double.infinity,
      padding: AppSpacing.cardPadding,
      decoration: const BoxDecoration(
        color: AppColors.surfaceMuted,
        borderRadius: AppSpacing.brLg,
      ),
      child: Text(
        message,
        style: theme.textTheme.bodyMedium
            ?.copyWith(color: AppColors.textSecondary),
      ),
    );
  }
}
