import 'package:courtly/core/app_flavor.dart';
import 'package:courtly/core/env/app_config.dart';
import 'package:courtly/core/theme/app_theme.dart';
import 'package:courtly/core/widgets/db_dropdown.dart';
import 'package:courtly/features/court_catalog/application/court_providers.dart';
import 'package:courtly/features/court_catalog/data/court_repository.dart';
import 'package:courtly/features/court_catalog/domain/court_models.dart';
import 'package:courtly/features/court_catalog/presentation/forms/court_form.dart';
import 'package:courtly/features/reference_data/domain/reference_models.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  const paris = City(id: 1, name: 'Paris', countryId: 1, countryName: 'France');
  const lyon = City(id: 2, name: 'Lyon', countryId: 1, countryName: 'France');
  const clay = SurfaceType(id: 1, name: 'Clay');
  const indoorType = CourtType(id: 1, name: 'Indoor');
  const lockers = Amenity(id: 1, name: 'Lockers');

  CourtFormLookups lookups({
    List<City> cities = const [paris, lyon],
    List<SurfaceType> surfaceTypes = const [clay],
    List<CourtType> courtTypes = const [indoorType],
    List<Amenity> amenities = const [lockers],
  }) =>
      CourtFormLookups(
        cities: cities,
        surfaceTypes: surfaceTypes,
        courtTypes: courtTypes,
        amenities: amenities,
      );

  Future<void> pumpCourtForm(
    WidgetTester tester, {
    required CourtFormLookups data,
    CourtRepository? repo,
  }) async {
    // The court form is a tall dialog (name, description, 3 FK dropdowns, the
    // map-location picker, amenity multi-select, price, image pane + three
    // toggles + actions). Give the test a viewport tall enough that the
    // "Create" action is on-screen and hit-testable.
    tester.view.physicalSize = const Size(1200, 2400);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          // The form reads the base URL for image thumbnails on every build;
          // appConfigProvider throws unless overridden, so seed a test value.
          appConfigProvider.overrideWithValue(
            const AppConfig(
              flavor: AppFlavor.admin,
              apiBaseUrl: 'http://localhost:5000',
            ),
          ),
          courtRepositoryProvider
              .overrideWithValue(repo ?? _FakeCourtRepository()),
          courtFormLookupsProvider.overrideWith((ref) async => data),
        ],
        child: MaterialApp(
          theme: AppTheme.light,
          home: Consumer(
            builder: (context, ref, _) => Scaffold(
              body: Center(
                child: ElevatedButton(
                  onPressed: () => showCourtForm(context, ref),
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
    await pumpCourtForm(tester, data: lookups());
    await tester.tap(find.widgetWithText(ElevatedButton, 'Create'));
    await tester.pumpAndSettle();
    expect(find.text('Name is required.'), findsOneWidget);
    expect(find.text('City is required.'), findsOneWidget);
    expect(find.text('Surface type is required.'), findsOneWidget);
    expect(find.text('Court type is required.'), findsOneWidget);
    expect(find.text('Price is required.'), findsOneWidget);
  });

  testWidgets('a non-positive price reports the format limit', (tester) async {
    await pumpCourtForm(tester, data: lookups());
    await tester.enterText(
        find.widgetWithText(TextFormField, 'Hourly price'), '0');
    await tester.tap(find.widgetWithText(ElevatedButton, 'Create'));
    await tester.pumpAndSettle();
    expect(find.text('Enter a price greater than 0.'), findsOneWidget);
  });

  testWidgets('City dropdown is populated from the lookups', (tester) async {
    await pumpCourtForm(tester, data: lookups());
    final dropdown =
        tester.widget<DbDropdown<City>>(find.byType(DbDropdown<City>));
    expect(dropdown.items, const [paris, lyon]);
  });

  testWidgets('empty surface lookup renders "No options available"',
      (tester) async {
    await pumpCourtForm(tester, data: lookups(surfaceTypes: const []));
    expect(find.text('No options available'), findsOneWidget);
    final dropdown = tester
        .widget<DbDropdown<SurfaceType>>(find.byType(DbDropdown<SurfaceType>));
    expect(dropdown.items, isEmpty);
  });
}

class _FakeCourtRepository implements CourtRepository {
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
    bool? underMaintenance,
  }) async =>
      const PagedResult<Court>(
        items: [],
        page: 1,
        pageSize: 20,
        totalCount: 0,
        hasNext: false,
        hasPrevious: false,
      );

  @override
  dynamic noSuchMethod(Invocation invocation) =>
      throw UnsupportedError('Unexpected repository call: ${invocation.memberName}');
}
