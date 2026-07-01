import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../../../core/widgets/async_value_view.dart';
import '../../../core/widgets/paginated_list_view.dart';
import '../../reference_data/domain/reference_models.dart' show PagedResult;
import '../../reviews/application/review_providers.dart';
import '../../reviews/domain/review_models.dart';
import 'widgets/review_tile.dart';

/// The full, paginated reviews list for a court — reached via "See all" on the
/// court-detail screen. Lives on its own route above the bottom-nav shell, so it
/// is a full screen with the standard back button. The current page is held
/// locally and re-watches [courtReviewsPageProvider] as it changes.
class ClientCourtReviewsScreen extends ConsumerStatefulWidget {
  const ClientCourtReviewsScreen({
    super.key,
    required this.courtId,
    this.courtName,
  });

  final int courtId;
  final String? courtName;

  @override
  ConsumerState<ClientCourtReviewsScreen> createState() =>
      _ClientCourtReviewsScreenState();
}

class _ClientCourtReviewsScreenState
    extends ConsumerState<ClientCourtReviewsScreen> {
  int _page = 1;

  @override
  Widget build(BuildContext context) {
    final value = ref.watch(
      courtReviewsPageProvider((courtId: widget.courtId, page: _page)),
    );

    return Scaffold(
      appBar: AppBar(title: Text(widget.courtName ?? 'Reviews')),
      body: AsyncValueView<PagedResult<Review>>(
        value: value,
        onRetry: () => ref.invalidate(
          courtReviewsPageProvider((courtId: widget.courtId, page: _page)),
        ),
        data: (paged) => PaginatedListView<Review>(
          items: paged.items,
          page: paged.page,
          pageSize: paged.pageSize,
          totalCount: paged.totalCount,
          hasNext: paged.hasNext,
          hasPrevious: paged.hasPrevious,
          onNextPage: () => setState(() => _page += 1),
          onPreviousPage: () => setState(() => _page -= 1),
          padding: const EdgeInsets.all(AppSpacing.lg),
          separator: const Divider(height: AppSpacing.lg),
          itemBuilder: (context, review, _) => ReviewTile(review: review),
          emptyPlaceholder: const _NoReviews(),
        ),
      ),
    );
  }
}

class _NoReviews extends StatelessWidget {
  const _NoReviews();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(AppSpacing.xl),
        child: Text(
          'No reviews yet',
          style:
              theme.textTheme.titleMedium?.copyWith(color: AppColors.textMuted),
        ),
      ),
    );
  }
}
