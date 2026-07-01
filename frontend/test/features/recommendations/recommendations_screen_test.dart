import 'package:courtly/core/app_flavor.dart';
import 'package:courtly/core/env/app_config.dart';
import 'package:courtly/core/theme/app_theme.dart';
import 'package:courtly/features/court_catalog/domain/court_models.dart';
import 'package:courtly/features/recommendations/application/recommendation_providers.dart';
import 'package:courtly/features/recommendations/data/recommendations_api.dart';
import 'package:courtly/features/recommendations/data/recommendations_repository.dart';
import 'package:courtly/features/recommendations/domain/recommendation_models.dart';
import 'package:courtly/features/recommendations/presentation/recommendations_screen.dart';
import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

/// F29 (recommendations): the screen renders the explainable summary + the
/// "Content-Based Filtering Active" badge, groups recommendations into reason
/// sections, and submits Yes/No feedback for the shown courts.
void main() {
  Court court(int id, String name, String surface) => Court(
        id: id,
        name: name,
        cityId: 1,
        cityName: 'Paris',
        countryId: 1,
        countryName: 'France',
        surfaceTypeId: 1,
        surfaceTypeName: surface,
        courtTypeId: 1,
        courtTypeName: 'Singles',
        isIndoor: false,
        isActive: true,
        isFeatured: false,
        hourlyPrice: 40,
      );

  final contentBatch = RecommendationBatch(
    summary: const RecommendationSummary(
      message: 'Based on your 3 bookings and recent searches, here are some matches.',
      isContentBased: true,
      basedOnBookings: 3,
    ),
    items: [
      Recommendation(
        court: court(1, 'Roland Garros Club', 'Clay'),
        score: 0.9,
        reasonCode: RecommendationReason.surface,
        reason: 'Because you like Clay',
      ),
      Recommendation(
        court: court(2, 'Sunny Side Court', 'Grass'),
        score: 0.5,
        reasonCode: RecommendationReason.timeBucket,
        reason: '☀️ Morning availability',
      ),
    ],
    totalCount: 2,
  );

  /// A fake repository that captures the feedback call (its `list` is unused —
  /// the screen's data comes from the overridden [recommendationsProvider]).
  Future<void> pump(
    WidgetTester tester,
    RecommendationBatch batch, {
    RecommendationsRepository? repository,
  }) async {
    tester.view.physicalSize = const Size(1200, 2600);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          appConfigProvider.overrideWithValue(
            const AppConfig(flavor: AppFlavor.client, apiBaseUrl: 'http://10.0.2.2:5000'),
          ),
          recommendationsProvider.overrideWith((ref) async => batch),
          if (repository != null)
            recommendationsRepositoryProvider.overrideWithValue(repository),
        ],
        child: MaterialApp(
          theme: AppTheme.light,
          home: const ClientRecommendationsScreen(),
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('renders the badge, summary and reason-grouped sections',
      (tester) async {
    await pump(tester, contentBatch);

    expect(find.text('Recommended Courts For You'), findsOneWidget);
    expect(find.textContaining('3 bookings'), findsOneWidget);
    expect(find.text('Content-Based Filtering Active'), findsOneWidget);
    // Each distinct reason becomes its own section header.
    expect(find.text('Because you like Clay'), findsOneWidget);
    expect(find.text('☀️ Morning availability'), findsOneWidget);
    // The court cards render.
    expect(find.text('Roland Garros Club'), findsWidgets);
    expect(find.text('Sunny Side Court'), findsWidgets);
    // Each card carries its own per-court feedback thumbs (one 👍 + one 👎 per card).
    expect(find.byIcon(Icons.thumb_up_outlined), findsNWidgets(2));
    expect(find.byIcon(Icons.thumb_down_outlined), findsNWidgets(2));
  });

  testWidgets("tapping a card's 👎 submits negative feedback for that court only",
      (tester) async {
    final repo = _FakeRecommendationsRepository();
    await pump(tester, contentBatch, repository: repo);

    // The first thumb-down belongs to the first card (Roland Garros Club, id 1).
    await tester.tap(find.byIcon(Icons.thumb_down_outlined).first);
    await tester.pumpAndSettle();

    expect(repo.calls, 1);
    expect(repo.lastIsHelpful, isFalse);
    expect(repo.lastCourtIds, [1]);
  });

  testWidgets('highlights the active thumb from the court feedback state',
      (tester) async {
    final rated = RecommendationBatch(
      summary: contentBatch.summary,
      items: [
        Recommendation(
          court: court(1, 'Roland Garros Club', 'Clay'),
          score: 0.9,
          reasonCode: RecommendationReason.surface,
          reason: 'Because you like Clay',
          userFeedback: false, // already disliked → filled thumb-down
        ),
      ],
      totalCount: 1,
    );

    await pump(tester, rated);

    expect(find.byIcon(Icons.thumb_down), findsOneWidget); // filled (active)
    expect(find.byIcon(Icons.thumb_up_outlined), findsOneWidget); // outlined (inactive)
  });

  testWidgets('hides the content-based badge on cold-start', (tester) async {
    final coldBatch = RecommendationBatch(
      summary: const RecommendationSummary(
        message: 'New here — showing popular and top-rated courts to get you started.',
        isContentBased: false,
        basedOnBookings: 0,
      ),
      items: [
        Recommendation(
          court: court(5, 'Center Court', 'Clay'),
          score: 1.0,
          reasonCode: RecommendationReason.popular,
          reason: 'Popular right now',
        ),
      ],
      totalCount: 1,
    );

    await pump(tester, coldBatch);

    expect(find.text('Content-Based Filtering Active'), findsNothing);
    expect(find.text('Popular right now'), findsOneWidget);
  });

  testWidgets('shows a friendly empty state when there are no picks',
      (tester) async {
    const empty = RecommendationBatch(
      summary: RecommendationSummary(
        message: 'No recommendations available.',
        isContentBased: false,
        basedOnBookings: 0,
      ),
      items: [],
      totalCount: 0,
    );

    await pump(tester, empty);

    expect(find.text('No recommendations yet'), findsOneWidget);
  });
}

class _FakeRecommendationsRepository extends RecommendationsRepository {
  _FakeRecommendationsRepository() : super(RecommendationsApi(Dio()));

  int calls = 0;
  bool? lastIsHelpful;
  List<int>? lastCourtIds;

  @override
  Future<void> submitFeedback({
    required bool isHelpful,
    required List<int> courtIds,
  }) async {
    calls++;
    lastIsHelpful = isHelpful;
    lastCourtIds = courtIds;
  }

  @override
  Future<RecommendationBatch> list({int page = 1, int pageSize = 20}) async =>
      throw UnimplementedError();
}
