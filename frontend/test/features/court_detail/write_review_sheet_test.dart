import 'package:courtly/core/theme/app_theme.dart';
import 'package:courtly/features/court_detail/presentation/widgets/write_review_sheet.dart';
import 'package:courtly/features/reviews/application/review_providers.dart';
import 'package:courtly/features/reviews/data/review_repository.dart';
import 'package:courtly/features/reviews/domain/review_models.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

/// F24 (write review): the form requires a star rating before it submits, and a
/// valid submit posts exactly once with the selected rating + the eligibility
/// reservation id (never chosen on the client).
void main() {
  Future<void> openSheet(WidgetTester tester, ReviewRepository repo) async {
    tester.view.physicalSize = const Size(800, 1400);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      ProviderScope(
        overrides: [reviewRepositoryProvider.overrideWithValue(repo)],
        child: MaterialApp(
          theme: AppTheme.light,
          home: Scaffold(
            body: Builder(
              builder: (context) => Center(
                child: ElevatedButton(
                  onPressed: () => showModalBottomSheet<Review>(
                    context: context,
                    isScrollControlled: true,
                    backgroundColor: Colors.transparent,
                    builder: (_) => const WriteReviewSheet(
                      reservationId: 7,
                      courtName: 'Grand Slam Court',
                    ),
                  ),
                  child: const Text('open'),
                ),
              ),
            ),
          ),
        ),
      ),
    );
    await tester.tap(find.text('open'));
    await tester.pumpAndSettle();
  }

  testWidgets('requires a rating before submitting', (tester) async {
    final repo = _FakeReviewRepository();
    await openSheet(tester, repo);

    await tester.tap(find.text('Submit review'));
    await tester.pumpAndSettle();

    expect(find.text('Please select a rating.'), findsOneWidget);
    expect(repo.createCalls, 0);
  });

  testWidgets('submits the selected rating exactly once', (tester) async {
    final repo = _FakeReviewRepository();
    await openSheet(tester, repo);

    // All five stars start empty; tap the 4th to select a rating of 4.
    await tester.tap(find.byIcon(Icons.star_border).at(3));
    await tester.pump();

    await tester.tap(find.text('Submit review'));
    await tester.pumpAndSettle();

    expect(repo.createCalls, 1);
    expect(repo.lastRating, 4);
    expect(repo.lastReservationId, 7);
  });
}

/// Records review-create calls so the test can assert how the form submits.
class _FakeReviewRepository implements ReviewRepository {
  int createCalls = 0;
  int? lastRating;
  int? lastReservationId;
  String? lastComment;

  @override
  Future<Review> create({
    required int reservationId,
    required int rating,
    String? comment,
  }) async {
    createCalls++;
    lastReservationId = reservationId;
    lastRating = rating;
    lastComment = comment;
    return Review(
      id: 99,
      courtId: 1,
      reviewerName: 'You',
      rating: rating,
      comment: comment,
      createdAtUtc: DateTime.utc(2026, 7, 1),
    );
  }

  @override
  dynamic noSuchMethod(Invocation invocation) =>
      throw UnsupportedError('Unexpected call: ${invocation.memberName}');
}
