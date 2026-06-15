import 'package:courtly/core/theme/app_colors.dart';
import 'package:courtly/core/widgets/status_badge.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('renders the label using its tone colors', (tester) async {
    const tone = StatusTone.success;
    final toneColors = StatusToneColors.of(tone);

    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: Center(
            child: StatusBadge(label: 'Confirmed', tone: tone),
          ),
        ),
      ),
    );

    // Label is shown with the tone's foreground color.
    expect(find.text('Confirmed'), findsOneWidget);
    final text = tester.widget<Text>(find.text('Confirmed'));
    expect(text.style?.color, toneColors.foreground);

    // The pill background uses the tone's background color.
    final pillExists = tester.widgetList<Container>(find.byType(Container)).any(
          (c) =>
              c.decoration is BoxDecoration &&
              (c.decoration as BoxDecoration).color == toneColors.background,
        );
    expect(pillExists, isTrue);
  });
}
