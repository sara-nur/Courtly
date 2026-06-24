import 'package:courtly/core/network/api_exception.dart';
import 'package:courtly/core/theme/app_theme.dart';
import 'package:courtly/core/widgets/confirm_dialog.dart';
import 'package:courtly/features/reference_data/application/reference_providers.dart';
import 'package:courtly/features/reference_data/data/reference_repository.dart';
import 'package:courtly/features/reference_data/domain/reference_models.dart';
import 'package:courtly/features/reference_data/presentation/settings_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  const france = Country(id: 1, name: 'France', isoCode: 'FRA');

  /// Pumps the Countries tab of the real [SettingsScreen] with one row, wired
  /// to a fake repository. [onDeleteCountry] drives the delete path the tab
  /// invokes through [referenceRepositoryProvider].
  Future<void> pumpCountriesTab(
    WidgetTester tester, {
    Future<void> Function(int id)? onDeleteCountry,
  }) async {
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          referenceRepositoryProvider.overrideWithValue(
            _FakeReferenceRepository(
              countries: const [france],
              onDeleteCountry: onDeleteCountry,
            ),
          ),
          countryLookupProvider.overrideWith((ref) async => const [france]),
        ],
        child: MaterialApp(
          theme: AppTheme.light,
          home: const Scaffold(body: SettingsScreen()),
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  Future<void> openConfirmAndConfirm(WidgetTester tester) async {
    await tester.tap(find.byTooltip('Delete'));
    await tester.pumpAndSettle();
    // ConfirmDialog is now showing.
    expect(find.byType(ConfirmDialog), findsOneWidget);
    expect(find.text('Delete "France"? This cannot be undone.'),
        findsOneWidget);
    // The destructive confirm button (not the title's close icon).
    await tester.tap(find.widgetWithText(ElevatedButton, 'Delete'));
    await tester.pumpAndSettle();
  }

  testWidgets('tapping delete opens a ConfirmDialog', (tester) async {
    await pumpCountriesTab(tester);

    await tester.tap(find.byTooltip('Delete'));
    await tester.pumpAndSettle();

    expect(find.byType(ConfirmDialog), findsOneWidget);
    expect(find.text('Delete "France"? This cannot be undone.'),
        findsOneWidget);
  });

  testWidgets('confirming calls the delete path and shows the deleted snackbar',
      (tester) async {
    var deletedId = -1;
    await pumpCountriesTab(
      tester,
      onDeleteCountry: (id) async => deletedId = id,
    );

    await openConfirmAndConfirm(tester);

    expect(deletedId, 1);
    expect(find.text('"France" deleted.'), findsOneWidget);
  });

  testWidgets(
      'delete-restrict ApiException surfaces the backend reason in a snackbar',
      (tester) async {
    await pumpCountriesTab(
      tester,
      onDeleteCountry: (_) async => throw const ApiException(
        message: 'Cannot delete France: it is referenced by cities.',
        statusCode: 409,
      ),
    );

    await openConfirmAndConfirm(tester);

    expect(
      find.text('Cannot delete France: it is referenced by cities.'),
      findsOneWidget,
    );
  });
}

/// In-memory stand-in so the screen runs without a network or secure storage.
class _FakeReferenceRepository implements ReferenceRepository {
  _FakeReferenceRepository({
    required this.countries,
    this.onDeleteCountry,
  });

  final List<Country> countries;
  final Future<void> Function(int id)? onDeleteCountry;

  @override
  Future<void> deleteCountry(int id) =>
      (onDeleteCountry ?? (_) async {})(id);

  @override
  Future<List<Country>> lookupCountries() async => countries;

  @override
  Future<PagedResult<Country>> listCountries({
    int page = 1,
    int pageSize = 20,
    String? search,
  }) async =>
      PagedResult<Country>(
        items: countries,
        page: 1,
        pageSize: 20,
        totalCount: countries.length,
        hasNext: false,
        hasPrevious: false,
      );

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
  dynamic noSuchMethod(Invocation invocation) => throw UnsupportedError(
      'Unexpected repository call: ${invocation.memberName}');
}
