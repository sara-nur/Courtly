import 'package:courtly/core/network/api_exception.dart';
import 'package:courtly/core/theme/app_theme.dart';
import 'package:courtly/core/widgets/db_dropdown.dart';
import 'package:courtly/features/reference_data/application/reference_providers.dart';
import 'package:courtly/features/reference_data/data/reference_repository.dart';
import 'package:courtly/features/reference_data/domain/reference_models.dart';
import 'package:courtly/features/reference_data/presentation/forms/city_form.dart';
import 'package:courtly/features/reference_data/presentation/settings_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  const franceFra = Country(id: 1, name: 'France', isoCode: 'FRA');
  const bosnia = Country(id: 2, name: 'Bosnia', isoCode: 'BIH');

  /// Pumps a host widget whose single button opens the City form via
  /// [showCityForm], so the form runs with a real [BuildContext]/[WidgetRef].
  Future<void> pumpCityForm(
    WidgetTester tester, {
    required List<Country> lookup,
    ReferenceRepository? repo,
  }) async {
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          referenceRepositoryProvider
              .overrideWithValue(repo ?? _FakeReferenceRepository()),
          countryLookupProvider.overrideWith((ref) async => lookup),
        ],
        child: MaterialApp(
          theme: AppTheme.light,
          home: Consumer(
            builder: (context, ref, _) => Scaffold(
              body: Center(
                child: ElevatedButton(
                  onPressed: () => showCityForm(context, ref),
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

  /// Pumps the real [SettingsScreen] and switches to the Cities tab, where the
  /// "+ Add" prerequisite block is computed from [countryLookupProvider].
  Future<void> pumpCitiesTab(
    WidgetTester tester, {
    required List<Country> lookup,
  }) async {
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          referenceRepositoryProvider
              .overrideWithValue(_FakeReferenceRepository()),
          countryLookupProvider.overrideWith((ref) async => lookup),
        ],
        child: MaterialApp(
          theme: AppTheme.light,
          home: const Scaffold(body: SettingsScreen()),
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('Cities'));
    await tester.pumpAndSettle();
  }

  testWidgets('empty submit shows the required message below the Name field',
      (tester) async {
    await pumpCityForm(tester, lookup: const [franceFra]);

    await tester.tap(find.widgetWithText(ElevatedButton, 'Create'));
    await tester.pumpAndSettle();

    expect(find.text('Name is required.'), findsOneWidget);
    expect(find.text('Country is required.'), findsOneWidget);
  });

  testWidgets('Country dropdown is populated from countryLookupProvider',
      (tester) async {
    await pumpCityForm(tester, lookup: const [franceFra, bosnia]);

    // The dropdown is enabled (has options) and exposes both country names.
    final dropdown = tester.widget<DbDropdown<Country>>(
      find.byType(DbDropdown<Country>),
    );
    expect(dropdown.items, const [franceFra, bosnia]);

    // Open the menu and confirm both names render (FK by name, never id).
    await tester.tap(find.byType(DbDropdown<Country>));
    await tester.pumpAndSettle();
    expect(find.text('France'), findsWidgets);
    expect(find.text('Bosnia'), findsWidgets);
  });

  testWidgets(
      'empty country lookup renders the dropdown as "No options available"',
      (tester) async {
    await pumpCityForm(tester, lookup: const []);

    expect(find.text('No options available'), findsOneWidget);
    final dropdown = tester.widget<DbDropdown<Country>>(
      find.byType(DbDropdown<Country>),
    );
    expect(dropdown.items, isEmpty);
  });

  testWidgets(
      'empty country lookup blocks "+ Add city" with the prerequisite reason',
      (tester) async {
    await pumpCitiesTab(tester, lookup: const []);

    // The reason is surfaced via the DisabledAction tooltip on "+ Add city".
    final tooltip = tester.widget<Tooltip>(find.byType(Tooltip));
    expect(tooltip.message,
        'Add a country first — cities require a country.');
  });
}

/// In-memory stand-in for the repository so the form runs without a network or
/// secure storage. The list controllers auto-load through this on build.
class _FakeReferenceRepository implements ReferenceRepository {
  @override
  Future<List<Country>> lookupCountries() async => const [];

  @override
  Future<PagedResult<City>> listCities({
    int page = 1,
    int pageSize = 20,
    String? search,
  }) async =>
      const PagedResult<City>(
        items: [],
        page: 1,
        pageSize: 20,
        totalCount: 0,
        hasNext: false,
        hasPrevious: false,
      );

  @override
  Future<PagedResult<Country>> listCountries({
    int page = 1,
    int pageSize = 20,
    String? search,
  }) async =>
      const PagedResult<Country>(
        items: [],
        page: 1,
        pageSize: 20,
        totalCount: 0,
        hasNext: false,
        hasPrevious: false,
      );

  @override
  Future<PagedResult<SurfaceType>> listSurfaceTypes({
    int page = 1,
    int pageSize = 20,
    String? search,
  }) async =>
      const PagedResult<SurfaceType>(
        items: [],
        page: 1,
        pageSize: 20,
        totalCount: 0,
        hasNext: false,
        hasPrevious: false,
      );

  @override
  Future<PagedResult<CourtType>> listCourtTypes({
    int page = 1,
    int pageSize = 20,
    String? search,
  }) async =>
      const PagedResult<CourtType>(
        items: [],
        page: 1,
        pageSize: 20,
        totalCount: 0,
        hasNext: false,
        hasPrevious: false,
      );

  @override
  Future<PagedResult<Amenity>> listAmenities({
    int page = 1,
    int pageSize = 20,
    String? search,
  }) async =>
      const PagedResult<Amenity>(
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
