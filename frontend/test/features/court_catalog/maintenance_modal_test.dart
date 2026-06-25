import 'package:courtly/core/enums/maintenance_status.dart';
import 'package:courtly/features/court_catalog/application/court_providers.dart';
import 'package:courtly/features/court_catalog/data/court_repository.dart';
import 'package:courtly/features/court_catalog/domain/court_models.dart';
import 'package:courtly/features/court_catalog/presentation/maintenance/maintenance_modal.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  final court = Court(
    id: 1,
    name: 'Center Court',
    cityId: 1,
    cityName: 'Sarajevo',
    countryId: 1,
    countryName: 'Bosnia',
    surfaceTypeId: 1,
    surfaceTypeName: 'Clay',
    courtTypeId: 1,
    courtTypeName: 'Tennis',
    isIndoor: false,
    isActive: true,
    isFeatured: false,
    hourlyPrice: 30,
    isUnderMaintenance: true,
    maintenanceReason: 'Resurfacing',
    maintenanceStartUtc: DateTime.utc(2026, 6, 25, 9),
  );

  Future<void> pumpModal(WidgetTester tester, _FakeCourtRepository fake) async {
    tester.view.physicalSize = const Size(1200, 1800);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      ProviderScope(
        overrides: [courtRepositoryProvider.overrideWithValue(fake)],
        child: MaterialApp(
          home: Scaffold(
            body: Consumer(
              builder: (context, ref, _) => ElevatedButton(
                onPressed: () => showCourtMaintenance(context, ref, court),
                child: const Text('open'),
              ),
            ),
          ),
        ),
      ),
    );
    await tester.tap(find.text('open'));
    await tester.pumpAndSettle();
  }

  testWidgets('renders the active in-progress window from history',
      (tester) async {
    final fake = _FakeCourtRepository(history: [
      CourtMaintenanceLog(
        id: 10,
        courtId: 1,
        status: MaintenanceStatus.inProgress,
        reason: 'Resurfacing',
        startUtc: DateTime.utc(2026, 6, 25, 9),
        performedByName: 'Super Admin',
        createdAtUtc: DateTime.utc(2026, 6, 25, 9),
      ),
    ]);

    await pumpModal(tester, fake);

    expect(find.text('In Progress'), findsOneWidget);
    expect(find.text('By Super Admin'), findsOneWidget);
    // The "Fix" transition affordance is offered for an in-progress window.
    expect(find.widgetWithText(TextButton, 'Fix'), findsOneWidget);
  });

  testWidgets('Fix confirms then calls the maintenance API', (tester) async {
    final fake = _FakeCourtRepository(history: [
      CourtMaintenanceLog(
        id: 10,
        courtId: 1,
        status: MaintenanceStatus.inProgress,
        reason: 'Resurfacing',
        startUtc: DateTime.utc(2026, 6, 25, 9),
        createdAtUtc: DateTime.utc(2026, 6, 25, 9),
      ),
    ]);

    await pumpModal(tester, fake);

    // Tap the row's "Fix" (a TextButton) → opens the confirm dialog.
    await tester.tap(find.widgetWithText(TextButton, 'Fix'));
    await tester.pumpAndSettle();

    // Confirm in the dialog (its confirm action is an ElevatedButton labelled "Fix").
    await tester.tap(find.widgetWithText(ElevatedButton, 'Fix'));
    await tester.pumpAndSettle();

    expect(fake.fixedCalls, [(1, 10)]);
    // The history reloads after the transition (provider invalidated) — listMaintenance is hit again.
    expect(fake.listMaintenanceCalls, greaterThanOrEqualTo(2));
  });

  testWidgets('an empty reason blocks submit with a below-field message',
      (tester) async {
    final fake = _FakeCourtRepository(history: const []);

    await pumpModal(tester, fake);

    await tester.tap(find.widgetWithText(ElevatedButton, 'Put under maintenance'));
    await tester.pumpAndSettle();

    expect(find.text('A maintenance reason is required.'), findsOneWidget);
    expect(fake.createCalls, 0); // never hit the API
  });

  testWidgets('a successful create clears the form without a false "required" error',
      (tester) async {
    final fake = _FakeCourtRepository(history: const []);

    await pumpModal(tester, fake);

    await tester.enterText(find.byType(TextFormField), 'Resurfacing');
    await tester.tap(find.widgetWithText(ElevatedButton, 'Put under maintenance'));
    await tester.pumpAndSettle();

    expect(fake.createCalls, 1); // the window WAS created
    // ...and the now-empty reason field must NOT show a stale validation error.
    expect(find.text('A maintenance reason is required.'), findsNothing);
  });
}

/// In-memory stand-in: serves the maintenance history, records Fix/create calls,
/// and returns an empty court page for the post-action grid refresh. Everything
/// else throws so an unexpected call is loud.
class _FakeCourtRepository implements CourtRepository {
  _FakeCourtRepository({required this.history});

  final List<CourtMaintenanceLog> history;
  final List<(int, int)> fixedCalls = [];
  int createCalls = 0;
  int listMaintenanceCalls = 0;

  @override
  Future<PagedResult<CourtMaintenanceLog>> listMaintenance(
    int courtId, {
    int page = 1,
    int pageSize = 50,
  }) async {
    listMaintenanceCalls++;
    return PagedResult<CourtMaintenanceLog>(
      items: history,
      page: 1,
      pageSize: pageSize,
      totalCount: history.length,
      hasNext: false,
      hasPrevious: false,
    );
  }

  @override
  Future<CourtMaintenanceLog> fixMaintenance(int courtId, int logId) async {
    fixedCalls.add((courtId, logId));
    return CourtMaintenanceLog(
      id: logId,
      courtId: courtId,
      status: MaintenanceStatus.completed,
      reason: 'Resurfacing',
      startUtc: DateTime.utc(2026, 6, 25, 9),
      endUtc: DateTime.utc(2026, 6, 25, 11),
      createdAtUtc: DateTime.utc(2026, 6, 25, 9),
    );
  }

  @override
  Future<CourtMaintenanceLog> createMaintenance(
    int courtId, {
    required String reason,
    DateTime? startUtc,
    DateTime? endUtc,
  }) async {
    createCalls++;
    return CourtMaintenanceLog(
      id: 99,
      courtId: courtId,
      status: MaintenanceStatus.inProgress,
      reason: reason,
      startUtc: startUtc ?? DateTime.utc(2026, 6, 25, 12),
      createdAtUtc: DateTime.utc(2026, 6, 25, 12),
    );
  }

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
  dynamic noSuchMethod(Invocation invocation) => throw UnsupportedError(
      'Unexpected repository call: ${invocation.memberName}');
}
