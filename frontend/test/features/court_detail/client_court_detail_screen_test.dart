import 'package:courtly/core/app_flavor.dart';
import 'package:courtly/core/env/app_config.dart';
import 'package:courtly/core/theme/app_theme.dart';
import 'package:courtly/core/widgets/disabled_action.dart';
import 'package:courtly/features/court_catalog/domain/court_models.dart';
import 'package:courtly/features/court_detail/application/court_detail_providers.dart';
import 'package:courtly/features/court_detail/presentation/client_court_detail_screen.dart';
import 'package:courtly/features/reviews/domain/review_models.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

/// F24 (court detail): the screen renders the court header (name, price,
/// location), rating, surface/indoor/amenity chips, "About this court" and the
/// reviews preview from a single [courtDetailProvider] AsyncValue; Book Now is
/// disabled (booking is F25); and "Write a review" appears only when the user is
/// eligible.
void main() {
  const court = Court(
    id: 1,
    name: 'Grand Slam Court',
    description: 'A premier hard court for testing.',
    cityId: 1,
    cityName: 'Santa Monica',
    countryId: 1,
    countryName: 'USA',
    surfaceTypeId: 1,
    surfaceTypeName: 'Hard',
    courtTypeId: 1,
    courtTypeName: 'Tennis',
    isIndoor: true,
    isActive: true,
    isFeatured: true,
    hourlyPrice: 45,
    avgRating: 4.5,
    reviewCount: 2,
  );

  final reviews = <Review>[
    Review(
      id: 1,
      courtId: 1,
      reviewerName: 'Alex Johnson',
      rating: 5,
      comment: 'Loved playing here.',
      createdAtUtc: DateTime.utc(2026, 6, 1),
    ),
    Review(
      id: 2,
      courtId: 1,
      reviewerName: 'Sam Lee',
      rating: 4,
      comment: 'Good surface.',
      createdAtUtc: DateTime.utc(2026, 5, 20),
    ),
  ];

  CourtDetailData buildData({required bool canReview}) => CourtDetailData(
        court: court,
        amenities: const [
          CourtAmenityLink(
            id: 1,
            courtId: 1,
            amenityId: 1,
            amenityName: 'Floodlights',
            isHighlighted: true,
          ),
        ],
        reviews: PagedResult<Review>(
          items: reviews,
          page: 1,
          pageSize: 3,
          totalCount: 2,
          hasNext: false,
          hasPrevious: false,
        ),
        eligibility: ReviewEligibility(
          canReview: canReview,
          reservationId: canReview ? 7 : null,
        ),
      );

  Future<void> pumpDetail(WidgetTester tester, {required bool canReview}) async {
    tester.view.physicalSize = const Size(1200, 2600);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          appConfigProvider.overrideWithValue(
            const AppConfig(
              flavor: AppFlavor.client,
              apiBaseUrl: 'http://10.0.2.2:5000',
            ),
          ),
          courtDetailProvider
              .overrideWith((ref, arg) async => buildData(canReview: canReview)),
        ],
        child: MaterialApp(
          theme: AppTheme.light,
          home: const ClientCourtDetailScreen(courtId: 1),
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('renders header, chips, about and the reviews preview',
      (tester) async {
    await pumpDetail(tester, canReview: false);

    expect(find.text('Grand Slam Court'), findsOneWidget);
    expect(find.text('Santa Monica, USA'), findsOneWidget);
    expect(find.text('About this court'), findsOneWidget);
    expect(find.text('A premier hard court for testing.'), findsOneWidget);

    // Chips: surface (uppercased), indoor/outdoor, amenity.
    expect(find.text('HARD'), findsOneWidget);
    expect(find.text('Indoor'), findsOneWidget);
    expect(find.text('Floodlights'), findsOneWidget);

    // Reviews preview.
    expect(find.text('Reviews'), findsOneWidget);
    expect(find.text('Alex Johnson'), findsOneWidget);
    expect(find.text('Loved playing here.'), findsOneWidget);

    // Sticky booking bar.
    expect(find.text('Total price'), findsOneWidget);
    expect(find.text('Book Now'), findsOneWidget);
  });

  testWidgets('Book Now is disabled until the booking flow exists',
      (tester) async {
    await pumpDetail(tester, canReview: false);

    final disabled = tester.widget<DisabledAction>(
      find.ancestor(
        of: find.text('Book Now'),
        matching: find.byType(DisabledAction),
      ),
    );
    expect(disabled.enabled, isFalse);
  });

  testWidgets('hides "Write a review" when the user is not eligible',
      (tester) async {
    await pumpDetail(tester, canReview: false);
    expect(find.text('Write a review'), findsNothing);
  });

  testWidgets('shows "Write a review" when the user is eligible',
      (tester) async {
    await pumpDetail(tester, canReview: true);
    expect(find.text('Write a review'), findsOneWidget);
  });
}
