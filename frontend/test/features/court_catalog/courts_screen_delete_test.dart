import 'package:courtly/core/network/api_exception.dart';
import 'package:courtly/core/widgets/confirm_dialog.dart';
import 'package:courtly/features/court_catalog/application/court_providers.dart';
import 'package:courtly/features/court_catalog/data/court_repository.dart';
import 'package:courtly/features/court_catalog/domain/court_models.dart';
import 'package:courtly/features/court_catalog/presentation/courts_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  const centerCourt = Court(
    id: 1,
    name: 'Center Court',
    description: null,
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
    isFeatured: false,
    hourlyPrice: 45,
    primaryImageUrl: null,
  );

  Future<void> pumpCourtsScreen(
    WidgetTester tester, {
    Future<void> Function(int id)? onDelete,
  }) async {
    // The court admin screen is a desktop-sized view (header + rich filter bar +
    // card grid). Give the test a realistic viewport so the single card's
    // Edit/Delete affordances are on-screen and hit-testable.
    tester.view.physicalSize = const Size(1200, 1600);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          courtRepositoryProvider.overrideWithValue(
            _FakeCourtRepository(courts: const [centerCourt], onDelete: onDelete),
          ),
          // Keep the lookups empty so the filter bar / Add button settle cleanly.
          cityLookupProvider.overrideWith((ref) async => const []),
          surfaceTypeLookupProvider.overrideWith((ref) async => const []),
          courtTypeLookupProvider.overrideWith((ref) async => const []),
          courtCountryLookupProvider.overrideWith((ref) async => const []),
          courtFormLookupsProvider.overrideWith(
            (ref) async => const CourtFormLookups(
                cities: [], surfaceTypes: [], courtTypes: []),
          ),
        ],
        child: const MaterialApp(home: Scaffold(body: CourtsScreen())),
      ),
    );
    await tester.pumpAndSettle();
  }

  Future<void> openConfirmAndConfirm(WidgetTester tester) async {
    await tester.tap(find.byTooltip('Delete'));
    await tester.pumpAndSettle();
    expect(find.byType(ConfirmDialog), findsOneWidget);
    expect(
        find.text('Delete "Center Court"? This cannot be undone.'),
        findsOneWidget);
    await tester.tap(find.widgetWithText(ElevatedButton, 'Delete'));
    await tester.pumpAndSettle();
  }

  testWidgets('tapping delete opens a ConfirmDialog', (tester) async {
    await pumpCourtsScreen(tester);
    await tester.tap(find.byTooltip('Delete'));
    await tester.pumpAndSettle();
    expect(find.byType(ConfirmDialog), findsOneWidget);
    expect(
        find.text('Delete "Center Court"? This cannot be undone.'),
        findsOneWidget);
  });

  testWidgets('confirming calls the delete path and shows the deleted snackbar',
      (tester) async {
    var deletedId = -1;
    await pumpCourtsScreen(tester, onDelete: (id) async => deletedId = id);
    await openConfirmAndConfirm(tester);
    expect(deletedId, 1);
    expect(find.text('"Center Court" deleted.'), findsOneWidget);
  });

  testWidgets('delete-restrict ApiException surfaces the backend reason',
      (tester) async {
    await pumpCourtsScreen(
      tester,
      onDelete: (_) async => throw const ApiException(
        message: 'Cannot delete Center Court: it has reservations.',
        statusCode: 409,
      ),
    );
    await openConfirmAndConfirm(tester);
    expect(
        find.text('Cannot delete Center Court: it has reservations.'),
        findsOneWidget);
  });
}

class _FakeCourtRepository implements CourtRepository {
  _FakeCourtRepository({required this.courts, this.onDelete});

  final List<Court> courts;
  final Future<void> Function(int id)? onDelete;

  @override
  Future<void> delete(int id) => (onDelete ?? (_) async {})(id);

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
  }) async =>
      PagedResult<Court>(
        items: courts,
        page: 1,
        pageSize: 20,
        totalCount: courts.length,
        hasNext: false,
        hasPrevious: false,
      );

  @override
  dynamic noSuchMethod(Invocation invocation) =>
      throw UnsupportedError('Unexpected repository call: ${invocation.memberName}');
}
