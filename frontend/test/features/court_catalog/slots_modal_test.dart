import 'package:courtly/core/enums/time_of_day_bucket.dart';
import 'package:courtly/features/court_catalog/application/court_providers.dart';
import 'package:courtly/features/court_catalog/data/court_repository.dart';
import 'package:courtly/features/court_catalog/domain/court_models.dart';
import 'package:courtly/features/court_catalog/presentation/slots/slots_modal.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  const court = Court(
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
    hourlyPrice: 20,
  );

  /// A day with one free morning slot (id 11) and one taken afternoon slot (id 12).
  DayAvailability defaultDay() => DayAvailability(
        courtId: 1,
        date: DateTime(2026, 6, 25),
        isCourtUnderMaintenance: false,
        buckets: [
          AvailabilityBucket(
            bucket: TimeOfDayBucket.morning,
            bucketName: 'Morning',
            slots: [
              AvailabilitySlot(
                id: 11,
                startUtc: DateTime.utc(2026, 6, 25, 8),
                endUtc: DateTime.utc(2026, 6, 25, 9),
                price: 20,
                isTaken: false,
              ),
            ],
          ),
          AvailabilityBucket(
            bucket: TimeOfDayBucket.afternoon,
            bucketName: 'Afternoon',
            slots: [
              AvailabilitySlot(
                id: 12,
                startUtc: DateTime.utc(2026, 6, 25, 14),
                endUtc: DateTime.utc(2026, 6, 25, 15),
                price: 20,
                isTaken: true,
              ),
            ],
          ),
          const AvailabilityBucket(
            bucket: TimeOfDayBucket.evening,
            bucketName: 'Evening',
            slots: [],
          ),
        ],
      );

  Future<void> pumpModal(WidgetTester tester, _FakeCourtRepository fake) async {
    tester.view.physicalSize = const Size(1300, 2200);
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
                onPressed: () => showCourtSlots(context, ref, court),
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

  testWidgets('renders the day buckets with a free slot and a Booked slot',
      (tester) async {
    final fake = _FakeCourtRepository(day: defaultDay());

    await pumpModal(tester, fake);

    expect(find.textContaining('Morning'), findsOneWidget);
    expect(find.textContaining('Afternoon'), findsOneWidget);
    // The taken slot is labelled and not removable; the free slot has a keyed remove.
    expect(find.text('Booked'), findsOneWidget);
    expect(find.byKey(const ValueKey('remove-slot-11')), findsOneWidget);
    expect(find.byKey(const ValueKey('remove-slot-12')), findsNothing);
  });

  testWidgets('removing a free slot confirms then calls the API and refreshes',
      (tester) async {
    final fake = _FakeCourtRepository(day: defaultDay());

    await pumpModal(tester, fake);

    await tester.tap(find.byKey(const ValueKey('remove-slot-11')));
    await tester.pumpAndSettle();
    // Confirm in the dialog (confirm action is an ElevatedButton labelled "Remove").
    await tester.tap(find.widgetWithText(ElevatedButton, 'Remove'));
    await tester.pumpAndSettle();

    expect(fake.removeSlotCalls, [(1, 11)]);
    // The day reloads after the remove (provider invalidated) → availability hit again.
    expect(fake.availabilityCalls, greaterThanOrEqualTo(2));
  });

  testWidgets('an out-of-range evening peak blocks generate with a below-field message',
      (tester) async {
    final fake = _FakeCourtRepository(day: defaultDay());

    await pumpModal(tester, fake);

    await tester.enterText(find.byType(TextFormField), '9');
    await tester.tap(find.widgetWithText(ElevatedButton, 'Generate'));
    await tester.pumpAndSettle();

    expect(find.text('Must be between 1.0 and 5.0.'), findsOneWidget);
    expect(fake.generateCalls, 0); // never hit the API
  });

  testWidgets('a valid generate calls the API', (tester) async {
    final fake = _FakeCourtRepository(day: defaultDay());

    await pumpModal(tester, fake);

    await tester.tap(find.widgetWithText(ElevatedButton, 'Generate'));
    await tester.pumpAndSettle();

    expect(fake.generateCalls, 1);
  });

  testWidgets('a court under maintenance shows the maintenance notice',
      (tester) async {
    final fake = _FakeCourtRepository(
      day: DayAvailability(
        courtId: 1,
        date: DateTime(2026, 6, 25),
        isCourtUnderMaintenance: true,
        buckets: const [
          AvailabilityBucket(
              bucket: TimeOfDayBucket.morning, bucketName: 'Morning', slots: []),
          AvailabilityBucket(
              bucket: TimeOfDayBucket.afternoon, bucketName: 'Afternoon', slots: []),
          AvailabilityBucket(
              bucket: TimeOfDayBucket.evening, bucketName: 'Evening', slots: []),
        ],
      ),
    );

    await pumpModal(tester, fake);

    expect(find.textContaining('under maintenance'), findsOneWidget);
  });
}

/// In-memory stand-in: serves a fixed day's availability and records generate /
/// remove calls. Everything else throws so an unexpected call is loud.
class _FakeCourtRepository implements CourtRepository {
  _FakeCourtRepository({required this.day});

  final DayAvailability day;
  int availabilityCalls = 0;
  int generateCalls = 0;
  final List<(int, int)> removeSlotCalls = [];

  @override
  Future<DayAvailability> availability(int courtId, DateTime date) async {
    availabilityCalls++;
    return day;
  }

  @override
  Future<GenerateSlotsResult> generateSlots(
    int courtId, {
    required DateTime fromDate,
    required DateTime toDate,
    required int openHour,
    required int closeHour,
    required int slotMinutes,
    double? eveningPeakMultiplier,
  }) async {
    generateCalls++;
    return const GenerateSlotsResult(createdCount: 5, skippedCount: 0);
  }

  @override
  Future<void> removeSlot(int courtId, int slotId) async {
    removeSlotCalls.add((courtId, slotId));
  }

  @override
  Future<RemoveSlotsResult> removeDaySlots(int courtId, DateTime date) async {
    return const RemoveSlotsResult(removedCount: 0, blockedCount: 0);
  }

  @override
  dynamic noSuchMethod(Invocation invocation) => throw UnsupportedError(
      'Unexpected repository call: ${invocation.memberName}');
}
