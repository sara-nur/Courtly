import 'package:courtly/features/court_catalog/application/court_providers.dart';
import 'package:courtly/features/court_catalog/data/court_repository.dart';
import 'package:courtly/features/court_catalog/domain/court_models.dart';
import 'package:courtly/features/court_catalog/presentation/courts_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  /// Pumps the real [CourtsScreen] with every FK lookup empty, so the
  /// "+ Add New Court" prerequisite block (computed from
  /// [courtFormLookupsProvider]) is the only [Tooltip] on screen — mirroring the
  /// City form's empty-country block. The court list is empty so no card
  /// edit/delete tooltips appear.
  Future<void> pumpCourtsScreen(
    WidgetTester tester, {
    required CourtFormLookups lookups,
  }) async {
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          courtRepositoryProvider.overrideWithValue(_FakeCourtRepository()),
          cityLookupProvider.overrideWith((ref) async => const []),
          surfaceTypeLookupProvider.overrideWith((ref) async => const []),
          courtTypeLookupProvider.overrideWith((ref) async => const []),
          courtCountryLookupProvider.overrideWith((ref) async => const []),
          courtFormLookupsProvider.overrideWith((ref) async => lookups),
        ],
        child: const MaterialApp(home: Scaffold(body: CourtsScreen())),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets(
      'empty prerequisite lookups block "+ Add New Court" with the reason',
      (tester) async {
    await pumpCourtsScreen(
      tester,
      lookups: const CourtFormLookups(
        cities: [],
        surfaceTypes: [],
        courtTypes: [],
        amenities: [],
      ),
    );

    // The reason is surfaced via the DisabledAction tooltip on the Add button.
    final tooltip = tester.widget<Tooltip>(find.byType(Tooltip));
    expect(
      tooltip.message,
      'Add a city, a surface type, a court type first — courts require them.',
    );
  });
}

/// In-memory stand-in so the screen runs without a network or secure storage.
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
  dynamic noSuchMethod(Invocation invocation) => throw UnsupportedError(
      'Unexpected repository call: ${invocation.memberName}');
}
