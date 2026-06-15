import 'package:courtly/core/theme/app_theme.dart';
import 'package:courtly/core/widgets/confirm_dialog.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  // Pumps a screen with a button that opens the dialog and records its result
  // into [resultRef]. Returns once the dialog is on screen.
  Future<void> pumpAndOpen(
    WidgetTester tester,
    List<bool?> resultRef,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        theme: AppTheme.light,
        home: Scaffold(
          body: Builder(
            builder: (context) => ElevatedButton(
              onPressed: () async {
                resultRef[0] = await ConfirmDialog.show(
                  context,
                  title: 'Delete court',
                  message: 'This action cannot be undone.',
                  confirmLabel: 'Delete',
                  destructive: true,
                );
              },
              child: const Text('open'),
            ),
          ),
        ),
      ),
    );
    await tester.tap(find.text('open'));
    await tester.pumpAndSettle();
  }

  testWidgets('shows the title and message', (tester) async {
    await pumpAndOpen(tester, [null]);
    expect(find.text('Delete court'), findsOneWidget);
    expect(find.text('This action cannot be undone.'), findsOneWidget);
  });

  testWidgets('returns true when confirmed', (tester) async {
    final result = <bool?>[null];
    await pumpAndOpen(tester, result);
    await tester.tap(find.text('Delete'));
    await tester.pumpAndSettle();
    expect(result[0], isTrue);
    expect(find.text('Delete court'), findsNothing); // dialog dismissed
  });

  testWidgets('returns false when cancelled', (tester) async {
    final result = <bool?>[null];
    await pumpAndOpen(tester, result);
    await tester.tap(find.text('Cancel'));
    await tester.pumpAndSettle();
    expect(result[0], isFalse);
  });
}
