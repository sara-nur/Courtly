import 'package:courtly/core/theme/app_theme.dart';
import 'package:courtly/core/widgets/map_picker_modal.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:latlong2/latlong.dart';

void main() {
  // Pumps a host with a button that opens the picker (seeded with [initial]) and
  // records the resolved value into [resultRef]. Uses bare [pump]s (never
  // pumpAndSettle) so the OSM tile fetch never blocks the test.
  Future<void> pumpAndOpen(
    WidgetTester tester,
    List<LatLng?> resultRef, {
    required LatLng initial,
  }) async {
    // A roomy viewport so the dialog + the "Use this location" action are all
    // on-screen and hit-testable.
    tester.view.physicalSize = const Size(900, 1200);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      MaterialApp(
        theme: AppTheme.light,
        home: Scaffold(
          body: Builder(
            builder: (context) => ElevatedButton(
              onPressed: () async {
                resultRef[0] = await MapPickerModal.show(
                  context,
                  initial: initial,
                );
              },
              child: const Text('open'),
            ),
          ),
        ),
      ),
    );
    await tester.tap(find.text('open'));
    // A couple of frames is enough to mount the dialog; tiles fail silently.
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 100));
  }

  testWidgets('returns the initial coordinate when "Use this location" is tapped',
      (tester) async {
    const sarajevo = LatLng(43.8563, 18.4131);
    final result = <LatLng?>[null];
    await pumpAndOpen(tester, result, initial: sarajevo);

    // The chosen coord is shown read-only (display only, never an editable box).
    expect(find.textContaining('43.856300'), findsOneWidget);

    await tester.tap(find.widgetWithText(ElevatedButton, 'Use this location'));
    await tester.pump();

    expect(result[0], isNotNull);
    expect(result[0]!.latitude, closeTo(sarajevo.latitude, 1e-9));
    expect(result[0]!.longitude, closeTo(sarajevo.longitude, 1e-9));
  });

  testWidgets('Cancel returns null', (tester) async {
    const sarajevo = LatLng(43.8563, 18.4131);
    final result = <LatLng?>[null];
    await pumpAndOpen(tester, result, initial: sarajevo);

    await tester.tap(find.widgetWithText(TextButton, 'Cancel'));
    await tester.pump();

    expect(result[0], isNull);
  });
}
