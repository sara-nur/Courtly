import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../../core/network/api_exception.dart';
import '../../../../core/theme/app_colors.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../core/widgets/app_text_field.dart';
import '../../../../core/widgets/form_scaffold.dart';
import '../../../../core/widgets/star_rating.dart';
import '../../../reviews/application/review_providers.dart';

/// The write-a-review form, shown as a modal bottom sheet from the court-detail
/// screen when the user has a completed, not-yet-reviewed booking for the court.
/// It collects a 1–5 star rating (required) + an optional comment and POSTs via
/// the review repository. On success it pops with the created [Review]; the
/// caller refreshes the detail data. The reservation it posts against comes from
/// the server's eligibility probe — never chosen on the client.
class WriteReviewSheet extends ConsumerStatefulWidget {
  const WriteReviewSheet({
    super.key,
    required this.reservationId,
    required this.courtName,
  });

  final int reservationId;
  final String courtName;

  @override
  ConsumerState<WriteReviewSheet> createState() => _WriteReviewSheetState();
}

class _WriteReviewSheetState extends ConsumerState<WriteReviewSheet> {
  final TextEditingController _commentController = TextEditingController();

  int _rating = 0;
  bool _submitting = false;
  String? _ratingError;
  String? _submitError;

  @override
  void dispose() {
    _commentController.dispose();
    super.dispose();
  }

  void _setRating(int value) {
    setState(() {
      _rating = value;
      _ratingError = null;
    });
  }

  Future<void> _submit() async {
    if (_rating == 0) {
      setState(() => _ratingError = 'Please select a rating.');
      return;
    }

    final raw = _commentController.text.trim();
    setState(() {
      _submitting = true;
      _submitError = null;
    });

    try {
      final review = await ref.read(reviewRepositoryProvider).create(
            reservationId: widget.reservationId,
            rating: _rating,
            comment: raw.isEmpty ? null : raw,
          );
      if (mounted) Navigator.of(context).pop(review);
    } on ApiException catch (e) {
      if (mounted) {
        setState(() {
          _submitting = false;
          _submitError = e.message;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      padding: EdgeInsets.only(bottom: MediaQuery.viewInsetsOf(context).bottom),
      child: FormScaffold(
        title: 'Write a review',
        subtitle: widget.courtName,
        onClose: _submitting ? null : () => Navigator.of(context).pop(),
        actions: [
          TextButton(
            onPressed: _submitting ? null : () => Navigator.of(context).pop(),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: _submitting ? null : _submit,
            child: _submitting
                ? const SizedBox(
                    width: AppSpacing.md,
                    height: AppSpacing.md,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : const Text('Submit review'),
          ),
        ],
        children: [
          Text('Your rating', style: theme.textTheme.titleSmall),
          const SizedBox(height: AppSpacing.xs),
          StarRating(
            rating: _rating.toDouble(),
            size: AppSpacing.xl,
            onRatingChanged: _submitting ? null : _setRating,
          ),
          if (_ratingError != null) ...[
            const SizedBox(height: AppSpacing.xxs),
            Text(
              _ratingError!,
              style: theme.textTheme.bodySmall
                  ?.copyWith(color: AppColors.danger),
            ),
          ],
          AppTextField(
            controller: _commentController,
            label: 'Comment (optional)',
            hint: 'Share your experience on this court',
            maxLines: 4,
            enabled: !_submitting,
          ),
          if (_submitError != null)
            Text(
              _submitError!,
              style:
                  theme.textTheme.bodySmall?.copyWith(color: AppColors.danger),
            ),
        ],
      ),
    );
  }
}
