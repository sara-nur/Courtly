import 'package:flutter/material.dart';

import '../theme/app_colors.dart';
import '../theme/app_spacing.dart';

/// A row of star icons used for both **display** (read-only average / per-review
/// rating) and **input** (the write-review form).
///
/// When [onRatingChanged] is null the widget is read-only and renders
/// full/half/empty stars for the (possibly fractional) [rating]. When
/// [onRatingChanged] is provided each star is tappable and the widget reports the
/// chosen 1–[maxRating] value — there is no zero state from a tap (the form
/// treats an untouched 0 as "no rating yet").
class StarRating extends StatelessWidget {
  const StarRating({
    super.key,
    required this.rating,
    this.maxRating = 5,
    this.size = AppSpacing.md,
    this.color = AppColors.warning,
    this.onRatingChanged,
  });

  /// The rating to display (fractional allowed) or the currently-selected value
  /// in input mode.
  final double rating;

  /// Number of stars (the scale). Defaults to a 5-star scale.
  final int maxRating;

  /// Star glyph size.
  final double size;

  /// Star (filled) color.
  final Color color;

  /// When non-null, the widget is interactive and reports the tapped 1..max value.
  final ValueChanged<int>? onRatingChanged;

  bool get _interactive => onRatingChanged != null;

  @override
  Widget build(BuildContext context) {
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        for (var i = 1; i <= maxRating; i++) _buildStar(i),
      ],
    );
  }

  Widget _buildStar(int position) {
    final icon = Icon(_iconFor(position), size: size, color: color);
    if (!_interactive) {
      return icon;
    }

    return InkResponse(
      onTap: () => onRatingChanged!(position),
      radius: size,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: AppSpacing.xxs / 2),
        child: icon,
      ),
    );
  }

  IconData _iconFor(int position) {
    // Interactive mode fills whole stars up to the selected value; display mode
    // shows a half star when the average lands between two whole values.
    if (_interactive) {
      return position <= rating ? Icons.star : Icons.star_border;
    }
    if (rating >= position) return Icons.star;
    if (rating >= position - 0.5) return Icons.star_half;
    return Icons.star_border;
  }
}
