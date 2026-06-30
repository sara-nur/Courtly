import 'package:courtly/core/app_flavor.dart';
import 'package:courtly/core/env/app_config.dart';
import 'package:courtly/core/theme/app_theme.dart';
import 'package:courtly/core/widgets/db_dropdown.dart';
import 'package:courtly/features/court_catalog/application/court_providers.dart';
import 'package:courtly/features/court_catalog/data/court_repository.dart';
import 'package:courtly/features/court_catalog/domain/court_models.dart';
import 'package:courtly/features/court_search/presentation/client_search_screen.dart';
import 'package:courtly/features/reference_data/domain/reference_models.dart';
import 'package:courtly/features/search_history/application/search_history_providers.dart';
import 'package:courtly/features/search_history/data/search_history_repository.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

/// F23 (Search): the screen lists courts from the repository, and applying a
/// filter via the modal records the search exactly once to the search-history
/// backend (the explicit-search side effect — pagination must not record).
void main() {
  const clay = SurfaceType(id: 11, name: 'Clay');

  Court court(int id, String name) => Court(
        id: id,
        name: name,
        cityId: 1,
        cityName: 'Paris',
        countryId: 1,
        countryName: 'France',
        surfaceTypeId: 11,
        surfaceTypeName: 'Clay',
        courtTypeId: 21,
        courtTypeName: 'Indoor',
        isIndoor: true,
        isActive: true,
        isFeatured: false,
        hourlyPrice: 30,
      );

  Future<void> pumpSearch(
    WidgetTester tester, {
    required _FakeCourtRepository courtRepo,
    required _RecordingSearchHistoryRepository historyRepo,
  }) async {
    tester.view.physicalSize = const Size(1200, 2400);
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
          courtRepositoryProvider.overrideWithValue(courtRepo),
          searchHistoryRepositoryProvider.overrideWithValue(historyRepo),
          // The filter modal reads these FK lookups; seed the surface list so a
          // surface can be picked, court types empty is fine for this flow.
          surfaceTypeLookupProvider.overrideWith((ref) async => const [clay]),
          courtTypeLookupProvider
              .overrideWith((ref) async => const <CourtType>[]),
        ],
        child: MaterialApp(
          theme: AppTheme.light,
          // The screen builds a bare Column (the client shell normally supplies
          // the Scaffold + AppBar + bottom nav), so wrap it for the test.
          home: const Scaffold(body: ClientSearchScreen()),
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('renders the court names from the repository page',
      (tester) async {
    final courtRepo = _FakeCourtRepository(
      [court(1, 'Riverside Arena'), court(2, 'Downtown Courts')],
    );
    await pumpSearch(
      tester,
      courtRepo: courtRepo,
      historyRepo: _RecordingSearchHistoryRepository(),
    );

    expect(find.text('Riverside Arena'), findsOneWidget);
    expect(find.text('Downtown Courts'), findsOneWidget);
    // The initial auto-load is not an explicit search, so it must NOT record.
  });

  testWidgets('applying a filter records the search exactly once with its args',
      (tester) async {
    final courtRepo = _FakeCourtRepository([court(1, 'Riverside Arena')]);
    final historyRepo = _RecordingSearchHistoryRepository();
    await pumpSearch(tester, courtRepo: courtRepo, historyRepo: historyRepo);

    // The initial build auto-loads but does NOT record.
    expect(historyRepo.calls, isEmpty);

    // Open the Filters modal.
    await tester.tap(find.byIcon(Icons.tune));
    await tester.pumpAndSettle();

    // Pick a surface so the recorded args carry a non-null surfaceTypeId.
    await tester.tap(find.byType(DbDropdown<SurfaceType>));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Clay').last);
    await tester.pumpAndSettle();

    // Apply -> controller.applyFilters -> fire-and-forget record(...).
    await tester.tap(find.widgetWithText(ElevatedButton, 'Apply'));
    await tester.pumpAndSettle();

    expect(historyRepo.calls, hasLength(1));
    final recorded = historyRepo.calls.single;
    expect(recorded.surfaceTypeId, 11);
    // No query was typed and no other control was touched.
    expect(recorded.courtTypeId, isNull);
    expect(recorded.minPrice, isNull);
    expect(recorded.maxPrice, isNull);
    expect(recorded.indoorOnly, isNull);
    expect(recorded.rawQuery, isNull);
  });
}

/// One captured `record(...)` invocation.
class _RecordedSearch {
  const _RecordedSearch({
    this.surfaceTypeId,
    this.courtTypeId,
    this.minPrice,
    this.maxPrice,
    this.indoorOnly,
    this.rawQuery,
  });

  final int? surfaceTypeId;
  final int? courtTypeId;
  final double? minPrice;
  final double? maxPrice;
  final bool? indoorOnly;
  final String? rawQuery;
}

/// Recording fake: captures every `record(...)` call so the test can assert it
/// fired exactly once with the expected args.
class _RecordingSearchHistoryRepository implements SearchHistoryRepository {
  final List<_RecordedSearch> calls = [];

  @override
  Future<void> record({
    int? surfaceTypeId,
    int? courtTypeId,
    double? minPrice,
    double? maxPrice,
    bool? indoorOnly,
    String? rawQuery,
  }) async {
    calls.add(_RecordedSearch(
      surfaceTypeId: surfaceTypeId,
      courtTypeId: courtTypeId,
      minPrice: minPrice,
      maxPrice: maxPrice,
      indoorOnly: indoorOnly,
      rawQuery: rawQuery,
    ));
  }

  @override
  dynamic noSuchMethod(Invocation invocation) =>
      throw UnsupportedError('Unexpected call: ${invocation.memberName}');
}

/// Stand-in court repository returning a fixed single page of [items].
class _FakeCourtRepository implements CourtRepository {
  _FakeCourtRepository(this.items);

  final List<Court> items;

  @override
  Future<PagedResult<Court>> list({
    int page = 1,
    int pageSize = 20,
    String? search,
    int? cityId,
    int? countryId,
    int? surfaceTypeId,
    int? courtTypeId,
    bool? isIndoor,
    bool? isActive,
    bool? isFeatured,
    double? minPrice,
    double? maxPrice,
    double? minRating,
    bool? underMaintenance,
  }) async =>
      PagedResult<Court>(
        items: items,
        page: 1,
        pageSize: pageSize,
        totalCount: items.length,
        hasNext: false,
        hasPrevious: false,
      );

  @override
  dynamic noSuchMethod(Invocation invocation) =>
      throw UnsupportedError('Unexpected call: ${invocation.memberName}');
}
