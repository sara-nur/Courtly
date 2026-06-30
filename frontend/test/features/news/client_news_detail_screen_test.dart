import 'package:courtly/core/app_flavor.dart';
import 'package:courtly/core/env/app_config.dart';
import 'package:courtly/core/theme/app_theme.dart';
import 'package:courtly/features/news/domain/news_models.dart';
import 'package:courtly/features/news/presentation/client_news_detail_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

/// F23 (News reader): tapping a Home news card opens the full article. The
/// detail screen renders the title, full body and byline from the passed-in
/// [News] (no refetch), and degrades to a friendly message when none is given.
void main() {
  final article = News(
    id: 5,
    title: 'Summer league sign-ups are open',
    text: 'Reserve your spot for the July round-robin. '
        'Spaces are limited and fill up fast.',
    imageUrl: null, // null avoids a network image fetch in the test
    publishedAtUtc: DateTime.utc(2026, 6, 20, 9, 0),
    isActive: true,
    authorName: 'Courtly Team',
  );

  Future<void> pump(WidgetTester tester, News? news) async {
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          appConfigProvider.overrideWithValue(
            const AppConfig(
              flavor: AppFlavor.client,
              apiBaseUrl: 'http://10.0.2.2:5000',
            ),
          ),
        ],
        child: MaterialApp(
          theme: AppTheme.light,
          home: ClientNewsDetailScreen(news: news),
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('renders the title, full body and author', (tester) async {
    await pump(tester, article);

    expect(find.text('Summer league sign-ups are open'), findsOneWidget);
    expect(
      find.textContaining('Reserve your spot for the July round-robin'),
      findsOneWidget,
    );
    expect(find.textContaining('Courtly Team'), findsOneWidget);
  });

  testWidgets('shows an unavailable message when no article is passed',
      (tester) async {
    await pump(tester, null);

    expect(find.text('This article is unavailable.'), findsOneWidget);
  });
}
