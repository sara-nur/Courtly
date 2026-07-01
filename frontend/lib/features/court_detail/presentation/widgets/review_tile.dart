import 'package:flutter/material.dart';

import '../../../../core/theme/app_colors.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../core/utils/formatters.dart';
import '../../../../core/widgets/star_rating.dart';
import '../../../reviews/domain/review_models.dart';

/// One posted [Review]: the reviewer's name + star rating on top, the date, and
/// the optional comment below. Used both inline on the detail screen and in the
/// full "See all reviews" list.
class ReviewTile extends StatelessWidget {
  const ReviewTile({super.key, required this.review});

  final Review review;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final comment = review.comment?.trim();

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Expanded(
              child: Text(
                review.reviewerName,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: theme.textTheme.titleSmall
                    ?.copyWith(fontWeight: FontWeight.w700),
              ),
            ),
            StarRating(rating: review.rating.toDouble(), size: AppSpacing.md),
          ],
        ),
        const SizedBox(height: AppSpacing.xxs),
        Text(
          Formatters.date(review.createdAtUtc.toLocal()),
          style: theme.textTheme.bodySmall?.copyWith(color: AppColors.textMuted),
        ),
        if (comment != null && comment.isNotEmpty) ...[
          const SizedBox(height: AppSpacing.xs),
          Text(
            comment,
            style: theme.textTheme.bodyMedium
                ?.copyWith(color: AppColors.textSecondary, height: 1.4),
          ),
        ],
      ],
    );
  }
}
