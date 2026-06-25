import 'dart:typed_data';

import 'package:courtly/core/theme/app_theme.dart';
import 'package:courtly/core/widgets/image_upload_field.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  // A minimal valid 1x1 PNG so Image.memory has real bytes to decode.
  final pngBytes = Uint8List.fromList(const [
    0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, //
    0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
    0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
    0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
    0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
    0x54, 0x78, 0x9C, 0x62, 0x00, 0x01, 0x00, 0x00,
    0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
    0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
    0x42, 0x60, 0x82,
  ]);

  Future<void> pumpField(
    WidgetTester tester, {
    required List<ExistingCourtImage> existing,
    required List<PendingImage> pending,
    void Function(int imageId)? onRemoveExisting,
    void Function(int index)? onRemovePending,
    void Function(int imageId)? onSetPrimary,
    VoidCallback? onPickFiles,
  }) async {
    await tester.pumpWidget(
      MaterialApp(
        theme: AppTheme.light,
        home: Scaffold(
          body: ImageUploadField(
            existing: existing,
            pending: pending,
            onPickFiles: onPickFiles ?? () {},
            onRemoveExisting: onRemoveExisting ?? (_) {},
            onRemovePending: onRemovePending ?? (_) {},
            onSetPrimary: onSetPrimary ?? (_) {},
          ),
        ),
      ),
    );
    // A couple of frames; the existing image's network fetch fails to the error
    // widget rather than blocking the test (no pumpAndSettle).
    await tester.pump();
  }

  testWidgets('renders the strip caption and an "Add images" button',
      (tester) async {
    await pumpField(
      tester,
      existing: const [
        ExistingCourtImage(
          imageId: 7,
          absoluteUrl: 'http://localhost:5000/api/images/7',
          isPrimary: true,
        ),
      ],
      pending: [PendingImage(bytes: pngBytes, filename: 'pick.png')],
    );

    expect(find.text('Images'), findsOneWidget);
    expect(find.widgetWithText(OutlinedButton, 'Add images'), findsOneWidget);
    // The pending pick decodes via Image.memory. The existing image renders via
    // CachedNetworkImage, which also builds an Image internally, so target the
    // MemoryImage specifically rather than counting all Image widgets.
    expect(
      find.byWidgetPredicate((w) => w is Image && w.image is MemoryImage),
      findsOneWidget,
    );
  });

  testWidgets('tapping the existing remove (X) invokes onRemoveExisting',
      (tester) async {
    int? removed;
    await pumpField(
      tester,
      existing: const [
        ExistingCourtImage(
          imageId: 7,
          absoluteUrl: 'http://localhost:5000/api/images/7',
          isPrimary: false,
        ),
      ],
      pending: const [],
      onRemoveExisting: (id) => removed = id,
    );

    await tester.tap(find.byTooltip('Remove').first);
    await tester.pump();
    expect(removed, 7);
  });

  testWidgets('tapping the star invokes onSetPrimary', (tester) async {
    int? promoted;
    await pumpField(
      tester,
      existing: const [
        ExistingCourtImage(
          imageId: 7,
          absoluteUrl: 'http://localhost:5000/api/images/7',
          isPrimary: false,
        ),
      ],
      pending: const [],
      onSetPrimary: (id) => promoted = id,
    );

    await tester.tap(find.byTooltip('Set as primary'));
    await tester.pump();
    expect(promoted, 7);
  });

  testWidgets('tapping a pending remove (X) invokes onRemovePending',
      (tester) async {
    int? removedIndex;
    await pumpField(
      tester,
      existing: const [],
      pending: [PendingImage(bytes: pngBytes, filename: 'pick.png')],
      onRemovePending: (i) => removedIndex = i,
    );

    await tester.tap(find.byTooltip('Remove').first);
    await tester.pump();
    expect(removedIndex, 0);
  });
}
