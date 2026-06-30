import 'package:courtly/core/app_flavor.dart';
import 'package:courtly/core/enums/reservation_status.dart';
import 'package:courtly/core/enums/time_of_day_bucket.dart';
import 'package:courtly/core/env/app_config.dart';
import 'package:courtly/core/theme/app_theme.dart';
import 'package:courtly/features/client_home/application/home_controller.dart';
import 'package:courtly/features/client_home/presentation/client_home_screen.dart';
import 'package:courtly/features/court_catalog/domain/court_models.dart';
import 'package:courtly/features/news/domain/news_models.dart';
import 'package:courtly/features/reservations/domain/reservation_models.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

/// F23 (Home): the screen renders its three sections from a single
/// [homeDataProvider] AsyncValue — featured courts, the customer's recent
/// bookings, and the latest news — under the "Ready to serve?" hero.
void main() {
  const featuredCourt = Court(
    id: 1,
    name: 'Riverside Arena',
    cityId: 1,
    cityName: 'Paris',
    countryId: 1,
    countryName: 'France',
    surfaceTypeId: 1,
    surfaceTypeName: 'Clay',
    courtTypeId: 1,
    courtTypeName: 'Indoor',
    isIndoor: true,
    isActive: true,
    isFeatured: true,
    hourlyPrice: 40,
  );

  final recentBooking = Reservation(
    id: 7,
    userId: 'u-1',
    userName: 'Alex Johnson',
    courtId: 1,
    courtName: 'Riverside Arena',
    timeSlotId: 30,
    slotStartUtc: DateTime.utc(2026, 7, 1, 8, 0),
    slotEndUtc: DateTime.utc(2026, 7, 1, 9, 0),
    bucket: TimeOfDayBucket.morning,
    status: ReservationStatus.confirmed,
    totalPrice: 40,
    isPaid: true,
    createdAtUtc: DateTime.utc(2026, 6, 25, 12, 0),
  );

  final newsItem = News(
    id: 3,
    title: 'Summer league sign-ups are open',
    text: 'Reserve your spot for the July round-robin.',
    imageUrl: null,
    publishedAtUtc: DateTime.utc(2026, 6, 20, 9, 0),
    isActive: true,
    authorName: 'Courtly Team',
  );

  Future<void> pumpHome(WidgetTester tester, HomeData data) async {
    // The home feed is a long ListView (hero, search bar, chips, carousel,
    // bookings, news); a tall viewport keeps every section laid out for the
    // text assertions below.
    tester.view.physicalSize = const Size(1200, 2400);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          // Client flavor + base url — the court/news cards read it to compose
          // absolute image URLs.
          appConfigProvider.overrideWithValue(
            const AppConfig(
              flavor: AppFlavor.client,
              apiBaseUrl: 'http://10.0.2.2:5000',
            ),
          ),
          // Seed the screen's single data source with sample content so it
          // renders the data state directly (no repositories needed).
          homeDataProvider.overrideWith((ref) async => data),
        ],
        child: MaterialApp(
          theme: AppTheme.light,
          // The screen builds a bare ListView (the client shell normally
          // supplies the Scaffold), so wrap it for the test.
          home: const Scaffold(body: ClientHomeScreen()),
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('renders the hero and one item from each home section',
      (tester) async {
    await pumpHome(
      tester,
      HomeData(
        featuredCourts: const [featuredCourt],
        recentBookings: [recentBooking],
        news: [newsItem],
      ),
    );

    // Hero.
    expect(find.text('Ready to serve?'), findsOneWidget);
    // Section headers prove all three sections rendered.
    expect(find.text('Featured Courts'), findsOneWidget);
    expect(find.text('Recent Bookings'), findsOneWidget);
    expect(find.text('News'), findsOneWidget);

    // Featured court name (the carousel card).
    expect(find.text('Riverside Arena'), findsWidgets);
    // Recent booking status badge.
    expect(find.text('Confirmed'), findsOneWidget);
    // News title.
    expect(find.text('Summer league sign-ups are open'), findsOneWidget);
  });

  testWidgets('shows the empty hints when a section has no items',
      (tester) async {
    await pumpHome(
      tester,
      const HomeData(featuredCourts: [], recentBookings: [], news: []),
    );

    expect(find.text('Ready to serve?'), findsOneWidget);
    expect(find.text('No featured courts yet'), findsOneWidget);
    expect(find.text('No bookings yet'), findsOneWidget);
    expect(find.text('No news yet'), findsOneWidget);
  });
}
