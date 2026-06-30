import 'package:courtly/core/app_flavor.dart';
import 'package:courtly/core/env/app_config.dart';
import 'package:courtly/core/theme/app_theme.dart';
import 'package:courtly/core/widgets/db_dropdown.dart';
import 'package:courtly/features/court_catalog/application/court_providers.dart';
import 'package:courtly/features/court_search/application/search_controller.dart';
import 'package:courtly/features/court_search/presentation/widgets/search_filter_modal.dart';
import 'package:courtly/features/reference_data/domain/reference_models.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

/// F23 KEY DoD test: the filter modal maps its controls to the right
/// [CourtSearchFilters] fields (the filter -> query-param mapping the search
/// controller forwards to the court API), and "Clear all" resets them.
void main() {
  const clay = SurfaceType(id: 11, name: 'Clay');
  const hard = SurfaceType(id: 12, name: 'Hard');
  const indoorType = CourtType(id: 21, name: 'Indoor');

  // Capture of the filters the modal emits via onApply.
  late CourtSearchFilters? applied;

  /// Pumps [SearchFilterModal] inside a tall viewport so the slider, the
  /// segmented control, the star row and the action buttons are all on-screen
  /// and hit-testable (mirrors the court_form viewport sizing).
  Future<void> pumpModal(
    WidgetTester tester, {
    CourtSearchFilters initial = const CourtSearchFilters(),
    List<SurfaceType> surfaceTypes = const [clay, hard],
    List<CourtType> courtTypes = const [indoorType],
  }) async {
    applied = null;
    tester.view.physicalSize = const Size(1200, 2400);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          // Client flavor + base url: the modal sits in the client app shell
          // and shares the same config-read contract as the rest of the tree.
          appConfigProvider.overrideWithValue(
            const AppConfig(
              flavor: AppFlavor.client,
              apiBaseUrl: 'http://10.0.2.2:5000',
            ),
          ),
          // Surface + court type dropdowns load from the catalog FK lookups;
          // seed them directly so the modal renders populated dropdowns.
          surfaceTypeLookupProvider.overrideWith((ref) async => surfaceTypes),
          courtTypeLookupProvider.overrideWith((ref) async => courtTypes),
        ],
        child: MaterialApp(
          theme: AppTheme.light,
          home: Scaffold(
            body: SearchFilterModal(
              initial: initial,
              onApply: (filters) => applied = filters,
            ),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('Apply emits filters carrying each control as a query param',
      (tester) async {
    await pumpModal(tester);

    // Surface: pick "Hard" (id 12) from the Surface dropdown.
    await tester.tap(find.byType(DbDropdown<SurfaceType>));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Hard').last);
    await tester.pumpAndSettle();

    // Price band: drag both thumbs off the bounds so minPrice/maxPrice resolve
    // (a value at a bound reads as "no limit" -> null).
    final slider = find.byType(RangeSlider);
    final sliderRect = tester.getRect(slider);
    // Start thumb: nudge inward from the far left so minPrice > min bound.
    await tester.dragFrom(
      sliderRect.centerLeft + const Offset(4, 0),
      Offset(sliderRect.width * 0.25, 0),
    );
    await tester.pumpAndSettle();
    // End thumb: nudge inward from the far right so maxPrice < max bound.
    await tester.dragFrom(
      sliderRect.centerRight - const Offset(4, 0),
      Offset(-sliderRect.width * 0.25, 0),
    );
    await tester.pumpAndSettle();

    // Indoor: toggle the "Indoor" segment -> indoorOnly = true. The segment is
    // a private-typed SegmentedButton, so target its label text directly.
    await tester.tap(find.text('Indoor'));
    await tester.pumpAndSettle();

    // Min rating: tap the 4th star -> minRating = 4.
    final stars = find.byIcon(Icons.star_border);
    await tester.tap(stars.at(3));
    await tester.pumpAndSettle();

    await tester.tap(find.widgetWithText(ElevatedButton, 'Apply'));
    await tester.pumpAndSettle();

    expect(applied, isNotNull);
    final f = applied!;
    // surface -> surfaceTypeId
    expect(f.surfaceTypeId, 12);
    // price band -> minPrice / maxPrice (both moved off their bounds)
    expect(f.minPrice, isNotNull);
    expect(f.maxPrice, isNotNull);
    expect(f.minPrice, greaterThan(0));
    expect(f.maxPrice, lessThan(100));
    expect(f.minPrice! < f.maxPrice!, isTrue);
    // indoor toggle -> indoorOnly
    expect(f.indoorOnly, isTrue);
    // star floor -> minRating
    expect(f.minRating, 4.0);
  });

  testWidgets('a value left at the slider bound stays null (no limit)',
      (tester) async {
    await pumpModal(tester);

    // Touch nothing but the Indoor toggle, then Apply: with the price slider
    // untouched (both thumbs on the bounds) min/maxPrice must be null.
    await tester.tap(find.text('Outdoor'));
    await tester.pumpAndSettle();

    await tester.tap(find.widgetWithText(ElevatedButton, 'Apply'));
    await tester.pumpAndSettle();

    final f = applied!;
    expect(f.minPrice, isNull);
    expect(f.maxPrice, isNull);
    expect(f.surfaceTypeId, isNull);
    expect(f.minRating, isNull);
    // Outdoor segment -> indoorOnly = false (distinct from null = "any").
    expect(f.indoorOnly, isFalse);
  });

  testWidgets('Clear all resets the controls then Apply emits an empty set',
      (tester) async {
    // Start from a fully-populated filter set so "Clear all" has work to do.
    await pumpModal(
      tester,
      initial: const CourtSearchFilters(
        query: 'downtown',
        surfaceTypeId: 11,
        courtTypeId: 21,
        minPrice: 20,
        maxPrice: 80,
        indoorOnly: true,
        minRating: 5,
      ),
    );

    await tester.tap(find.widgetWithText(TextButton, 'Clear all'));
    await tester.pumpAndSettle();

    await tester.tap(find.widgetWithText(ElevatedButton, 'Apply'));
    await tester.pumpAndSettle();

    final f = applied!;
    expect(f.surfaceTypeId, isNull);
    expect(f.courtTypeId, isNull);
    expect(f.minPrice, isNull);
    expect(f.maxPrice, isNull);
    expect(f.indoorOnly, isNull);
    expect(f.minRating, isNull);
    // The modal does not own the free-text query, so it is preserved untouched.
    expect(f.query, 'downtown');
  });
}
