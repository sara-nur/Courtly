/// Immutable domain models for Feature 29 (client recommendations).
///
/// Mirror the backend recommender contracts (camelCase keys, manual `fromJson` —
/// the codebase convention). A [Recommendation] pairs a full [Court] (reused from
/// the catalog so the same card renders) with its score and an explainable reason
/// (a machine [reasonCode] the UI can style + a ready-to-show [reason] string).
/// [RecommendationBatch] unpacks the endpoint envelope — the explainable
/// [summary] plus the paged items (`page.items` / `page.totalCount`).
library;

import '../../court_catalog/domain/court_models.dart';

/// Why a court was recommended — mirrors the backend `RecommendationReason` enum
/// (int wire values 0..6). The first five are content signals; the last two are
/// the popularity fallbacks (cold-start / no content signal).
enum RecommendationReason {
  surface,
  courtType,
  timeBucket,
  price,
  indoor,
  popular,
  topRated;

  static RecommendationReason fromWire(int value) =>
      (value >= 0 && value < values.length) ? values[value] : popular;

  /// True for the popularity fallbacks (used to hide the "content-based" framing
  /// on an individual card when needed).
  bool get isPopularity =>
      this == RecommendationReason.popular || this == RecommendationReason.topRated;
}

/// One recommended court with its explainable reason. [userFeedback] is the
/// caller's current per-court rating (null = not rated, true = 👍, false = 👎), so
/// the card can highlight the active thumb.
class Recommendation {
  const Recommendation({
    required this.court,
    required this.score,
    required this.reasonCode,
    required this.reason,
    this.userFeedback,
  });

  final Court court;
  final double score;
  final RecommendationReason reasonCode;
  final String reason;
  final bool? userFeedback;

  factory Recommendation.fromJson(Map<String, dynamic> json) => Recommendation(
        court: Court.fromJson((json['court'] as Map).cast<String, dynamic>()),
        score: (json['score'] as num?)?.toDouble() ?? 0,
        reasonCode:
            RecommendationReason.fromWire((json['reasonCode'] as num?)?.toInt() ?? 5),
        reason: json['reason'] as String? ?? 'Recommended for you',
        userFeedback: json['userFeedback'] as bool?,
      );
}

/// The one-line explanation shown atop the screen. [isContentBased] drives the
/// "Content-Based Filtering Active" badge (false on cold-start).
class RecommendationSummary {
  const RecommendationSummary({
    required this.message,
    required this.isContentBased,
    required this.basedOnBookings,
  });

  final String message;
  final bool isContentBased;
  final int basedOnBookings;

  factory RecommendationSummary.fromJson(Map<String, dynamic> json) =>
      RecommendationSummary(
        message: json['message'] as String? ?? '',
        isContentBased: json['isContentBased'] as bool? ?? false,
        basedOnBookings: (json['basedOnBookings'] as num?)?.toInt() ?? 0,
      );
}

/// The recommendations endpoint envelope: the [summary] plus the ranked [items]
/// (unpacked from the nested `page`), and the total number of recommendations.
class RecommendationBatch {
  const RecommendationBatch({
    required this.summary,
    required this.items,
    required this.totalCount,
  });

  final RecommendationSummary summary;
  final List<Recommendation> items;
  final int totalCount;

  factory RecommendationBatch.fromJson(Map<String, dynamic> json) {
    final page = (json['page'] as Map?)?.cast<String, dynamic>() ?? const {};
    final rawItems = (page['items'] as List?) ?? const <dynamic>[];
    return RecommendationBatch(
      summary: RecommendationSummary.fromJson(
          (json['summary'] as Map?)?.cast<String, dynamic>() ?? const {}),
      items: rawItems
          .map((e) => Recommendation.fromJson((e as Map).cast<String, dynamic>()))
          .toList(growable: false),
      totalCount: (page['totalCount'] as num?)?.toInt() ?? rawItems.length,
    );
  }
}
