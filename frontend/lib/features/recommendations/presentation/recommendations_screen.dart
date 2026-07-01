import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../app/router/client_router.dart';
import '../../../core/network/api_exception.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../../../core/widgets/async_value_view.dart';
import '../../court_catalog/presentation/widgets/client_court_card.dart';
import '../application/recommendation_providers.dart';
import '../domain/recommendation_models.dart';

/// The "Recommended Courts For You" screen (F29, mockup p.10 §3.2.5). Reached from
/// the Home tab and pushed above the bottom-nav shell, so it gets the standard
/// back button (rubric §6). Renders the explainable summary + a "Content-Based
/// Filtering Active" badge and the recommendations grouped into **reason sections**
/// ("Because you like Clay", "☀️ Morning availability", "Popular right now"). Each
/// card carries its own 👍/👎 — feedback is **per court**: a 👎 demotes just that
/// court on the next fetch (it sinks to the bottom), a 👍 nudges it up.
class ClientRecommendationsScreen extends ConsumerStatefulWidget {
  const ClientRecommendationsScreen({super.key});

  @override
  ConsumerState<ClientRecommendationsScreen> createState() =>
      _ClientRecommendationsScreenState();
}

class _ClientRecommendationsScreenState
    extends ConsumerState<ClientRecommendationsScreen> {
  /// Height of a reason section's horizontal carousel: the court card (150px image
  /// + name/price block) plus the per-card feedback row.
  static const double _carouselHeight = 300;
  static const double _cardWidth = 180;

  bool _submitting = false;

  Future<void> _submitFeedback(bool isHelpful, int courtId) async {
    if (_submitting) {
      return;
    }
    setState(() => _submitting = true);
    try {
      await ref
          .read(recommendationsRepositoryProvider)
          .submitFeedback(isHelpful: isHelpful, courtIds: [courtId]);
      if (!mounted) {
        return;
      }
      // Re-fetch so the feedback takes effect immediately — a 👎 sinks that court
      // to the bottom, a 👍 nudges it up.
      ref.invalidate(recommendationsProvider);
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(
        content: Text(isHelpful ? 'Thanks — we’ll show more like this.' : 'Got it — we’ll show this less.'),
        duration: const Duration(seconds: 2),
      ));
    } on ApiException catch (e) {
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text(e.message)));
    } finally {
      if (mounted) {
        setState(() => _submitting = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final value = ref.watch(recommendationsProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('Recommended For You')),
      body: AsyncValueView<RecommendationBatch>(
        value: value,
        onRetry: () => ref.invalidate(recommendationsProvider),
        data: _buildContent,
      ),
    );
  }

  Widget _buildContent(RecommendationBatch batch) {
    if (batch.items.isEmpty) {
      return const _EmptyState();
    }

    final groups = _groupByReason(batch.items);

    final children = <Widget>[
      Padding(
        padding: const EdgeInsets.fromLTRB(
            AppSpacing.lg, AppSpacing.lg, AppSpacing.lg, 0),
        child: _Header(summary: batch.summary),
      ),
      const SizedBox(height: AppSpacing.lg),
    ];

    for (final entry in groups.entries) {
      children.add(_ReasonSection(
        reason: entry.key,
        items: entry.value,
        carouselHeight: _carouselHeight,
        cardWidth: _cardWidth,
        submitting: _submitting,
        onVote: _submitFeedback,
      ));
      children.add(const SizedBox(height: AppSpacing.xl));
    }

    return ListView(padding: const EdgeInsets.only(bottom: AppSpacing.lg), children: children);
  }

  /// Groups the ranked recommendations by their (already human) reason string,
  /// preserving rank order — so the sections come out as the mockup shows them.
  static Map<String, List<Recommendation>> _groupByReason(
      List<Recommendation> items) {
    final map = <String, List<Recommendation>>{};
    for (final r in items) {
      (map[r.reason] ??= <Recommendation>[]).add(r);
    }
    return map;
  }
}

/// The screen heading, the explainable summary line, and the "Content-Based
/// Filtering Active" badge (only shown when the picks are content-based).
class _Header extends StatelessWidget {
  const _Header({required this.summary});

  final RecommendationSummary summary;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          'Recommended Courts For You',
          style: theme.textTheme.headlineSmall?.copyWith(fontWeight: FontWeight.w800),
        ),
        const SizedBox(height: AppSpacing.xs),
        Text(
          summary.message,
          style: theme.textTheme.bodyMedium?.copyWith(color: AppColors.textSecondary),
        ),
        if (summary.isContentBased) ...[
          const SizedBox(height: AppSpacing.sm),
          const _ContentBasedBadge(),
        ],
      ],
    );
  }
}

/// The "Content-Based Filtering Active" pill from the mockup.
class _ContentBasedBadge extends StatelessWidget {
  const _ContentBasedBadge();

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(
          horizontal: AppSpacing.sm, vertical: AppSpacing.xs),
      decoration: const BoxDecoration(
        color: AppColors.primarySoft,
        borderRadius: AppSpacing.brPill,
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          const Icon(Icons.auto_awesome, size: AppSpacing.md, color: AppColors.primary),
          const SizedBox(width: AppSpacing.xs),
          Text(
            'Content-Based Filtering Active',
            style: Theme.of(context).textTheme.labelMedium?.copyWith(
                  color: AppColors.primaryDark,
                  fontWeight: FontWeight.w700,
                ),
          ),
        ],
      ),
    );
  }
}

/// One reason group ("Because you like Clay", …): a header + a horizontal carousel
/// of the courts recommended for that reason, each with its own feedback thumbs.
class _ReasonSection extends StatelessWidget {
  const _ReasonSection({
    required this.reason,
    required this.items,
    required this.carouselHeight,
    required this.cardWidth,
    required this.submitting,
    required this.onVote,
  });

  final String reason;
  final List<Recommendation> items;
  final double carouselHeight;
  final double cardWidth;
  final bool submitting;
  final void Function(bool isHelpful, int courtId) onVote;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: AppSpacing.lg),
          child: Text(
            reason,
            style: theme.textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w700),
          ),
        ),
        const SizedBox(height: AppSpacing.sm),
        SizedBox(
          height: carouselHeight,
          child: ListView.separated(
            scrollDirection: Axis.horizontal,
            padding: const EdgeInsets.symmetric(horizontal: AppSpacing.lg),
            itemCount: items.length,
            separatorBuilder: (_, __) => const SizedBox(width: AppSpacing.sm),
            itemBuilder: (context, i) => _RecommendationCard(
              recommendation: items[i],
              width: cardWidth,
              submitting: submitting,
              onVote: onVote,
            ),
          ),
        ),
      ],
    );
  }
}

/// A recommended court card + its per-court 👍/👎 feedback row. Tapping a thumb
/// records feedback for THIS court only; the active thumb is highlighted from the
/// court's current [Recommendation.userFeedback].
class _RecommendationCard extends StatelessWidget {
  const _RecommendationCard({
    required this.recommendation,
    required this.width,
    required this.submitting,
    required this.onVote,
  });

  final Recommendation recommendation;
  final double width;
  final bool submitting;
  final void Function(bool isHelpful, int courtId) onVote;

  @override
  Widget build(BuildContext context) {
    final court = recommendation.court;
    final feedback = recommendation.userFeedback;
    return SizedBox(
      width: width,
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          ClientCourtCard(
            court: court,
            width: width,
            onTap: () => context.push(ClientRoutes.courtDetailPath(court.id)),
          ),
          const SizedBox(height: AppSpacing.xs),
          Row(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              _ThumbButton(
                icon: Icons.thumb_up,
                outlinedIcon: Icons.thumb_up_outlined,
                active: feedback == true,
                activeColor: AppColors.primary,
                tooltip: 'Helpful',
                onTap: submitting ? null : () => onVote(true, court.id),
              ),
              const SizedBox(width: AppSpacing.sm),
              _ThumbButton(
                icon: Icons.thumb_down,
                outlinedIcon: Icons.thumb_down_outlined,
                active: feedback == false,
                activeColor: AppColors.danger,
                tooltip: 'Not helpful',
                onTap: submitting ? null : () => onVote(false, court.id),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

/// A single thumb (up or down) that fills in when it is the court's active rating.
class _ThumbButton extends StatelessWidget {
  const _ThumbButton({
    required this.icon,
    required this.outlinedIcon,
    required this.active,
    required this.activeColor,
    required this.tooltip,
    required this.onTap,
  });

  final IconData icon;
  final IconData outlinedIcon;
  final bool active;
  final Color activeColor;
  final String tooltip;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    return IconButton(
      onPressed: onTap,
      tooltip: tooltip,
      visualDensity: VisualDensity.compact,
      icon: Icon(
        active ? icon : outlinedIcon,
        color: active ? activeColor : AppColors.textMuted,
        size: AppSpacing.lg,
      ),
    );
  }
}

/// Shown when the user has no recommendations yet (e.g. brand-new account with no
/// courts to rank) — a friendly nudge rather than an empty screen.
class _EmptyState extends StatelessWidget {
  const _EmptyState();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(AppSpacing.xl),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(Icons.auto_awesome_outlined,
                size: AppSpacing.xxl, color: AppColors.textMuted),
            const SizedBox(height: AppSpacing.md),
            Text(
              'No recommendations yet',
              style: theme.textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w700),
            ),
            const SizedBox(height: AppSpacing.xs),
            Text(
              'Book a court or search around, and we’ll suggest courts you’ll love.',
              textAlign: TextAlign.center,
              style: theme.textTheme.bodyMedium?.copyWith(color: AppColors.textSecondary),
            ),
          ],
        ),
      ),
    );
  }
}
