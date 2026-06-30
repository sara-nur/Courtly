import 'package:courtly/core/app_flavor.dart';
import 'package:courtly/core/env/app_config.dart';
import 'package:courtly/core/theme/app_theme.dart';
import 'package:courtly/features/news/application/news_providers.dart';
import 'package:courtly/features/news/data/news_repository.dart';
import 'package:courtly/features/news/presentation/forms/news_form.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

/// Feature 21 DoD (auto): the News form shows server-side-equivalent validation
/// messages below their fields, and the image is required on create. Mirrors the
/// court_catalog form test harness (a tall viewport so the submit action is
/// hit-testable, the repository faked so no real network call happens).
void main() {
  Future<void> pumpNewsForm(WidgetTester tester) async {
    tester.view.physicalSize = const Size(1200, 2200);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          // The form reads the base URL for the image thumbnail on every build;
          // appConfigProvider throws unless overridden, so seed a test value.
          appConfigProvider.overrideWithValue(
            const AppConfig(
              flavor: AppFlavor.admin,
              apiBaseUrl: 'http://localhost:5000',
            ),
          ),
          newsRepositoryProvider.overrideWithValue(_FakeNewsRepository()),
        ],
        child: MaterialApp(
          theme: AppTheme.light,
          home: Consumer(
            builder: (context, ref, _) => Scaffold(
              body: Center(
                child: ElevatedButton(
                  onPressed: () => showNewsForm(context, ref),
                  child: const Text('Open'),
                ),
              ),
            ),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(ElevatedButton, 'Open'));
    await tester.pumpAndSettle();
  }

  testWidgets('empty submit shows required messages below the fields',
      (tester) async {
    await pumpNewsForm(tester);

    await tester.tap(find.widgetWithText(ElevatedButton, 'Publish'));
    await tester.pumpAndSettle();

    expect(find.text('Title is required.'), findsOneWidget);
    expect(find.text('Content is required.'), findsOneWidget);
    // The image is required on create.
    expect(find.text('An image is required.'), findsOneWidget);
  });

  testWidgets('valid text still blocks submit until an image is picked',
      (tester) async {
    await pumpNewsForm(tester);

    await tester.enterText(
        find.widgetWithText(TextFormField, 'Title'), 'Season opener');
    await tester.enterText(
        find.widgetWithText(TextFormField, 'Content'), 'The courts are open.');
    await tester.tap(find.widgetWithText(ElevatedButton, 'Publish'));
    await tester.pumpAndSettle();

    // Text errors clear, but the missing image still blocks submission.
    expect(find.text('Title is required.'), findsNothing);
    expect(find.text('Content is required.'), findsNothing);
    expect(find.text('An image is required.'), findsOneWidget);
  });
}

/// A repository that fails loudly if any method is hit — these tests never reach
/// a network call (validation blocks submit first).
class _FakeNewsRepository implements NewsRepository {
  @override
  dynamic noSuchMethod(Invocation invocation) => throw UnsupportedError(
        'Unexpected repository call: ${invocation.memberName}',
      );
}
