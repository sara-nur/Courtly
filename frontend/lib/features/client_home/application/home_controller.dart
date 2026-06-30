import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../court_catalog/application/court_providers.dart';
import '../../court_catalog/domain/court_models.dart';
import '../../news/application/news_providers.dart';
import '../../news/domain/news_models.dart';
import '../../reservations/application/reservation_providers.dart';
import '../../reservations/domain/reservation_models.dart';

/// The three independent collections backing the client Home screen (F23):
/// the featured-courts carousel, the signed-in customer's recent bookings, and
/// the latest published news. All three are loaded together (see
/// [homeDataProvider]) so the screen renders from a single [AsyncValue].
class HomeData {
  const HomeData({
    required this.featuredCourts,
    required this.recentBookings,
    required this.news,
  });

  final List<Court> featuredCourts;
  final List<Reservation> recentBookings;
  final List<News> news;
}

/// Page sizes for the home feed (single source — no magic numbers).
const int kHomeFeaturedCourtsSize = 10;
const int kHomeRecentBookingsSize = 5;
const int kHomeNewsSize = 5;

/// Loads the Home screen's three sections **in parallel** with [Future.wait].
///
/// Each repository is read directly (not via the sibling preview providers) so
/// the three fetches are truly concurrent under one [Future.wait] rather than
/// serialized through separate provider builds. Any failure surfaces as the
/// provider's error state, which the screen renders with a retry.
final homeDataProvider = FutureProvider<HomeData>((ref) async {
  final courtRepository = ref.watch(courtRepositoryProvider);
  final reservationRepository = ref.watch(reservationRepositoryProvider);
  final newsRepository = ref.watch(newsRepositoryProvider);

  final results = await Future.wait([
    courtRepository.list(isFeatured: true, pageSize: kHomeFeaturedCourtsSize),
    reservationRepository.listMine(pageSize: kHomeRecentBookingsSize),
    newsRepository.listPublished(pageSize: kHomeNewsSize),
  ]);

  return HomeData(
    featuredCourts: (results[0] as PagedResult<Court>).items,
    recentBookings: (results[1] as PagedResult<Reservation>).items,
    news: (results[2] as PagedResult<News>).items,
  );
});
