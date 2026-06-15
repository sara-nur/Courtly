import 'package:courtly/core/theme/app_theme.dart';
import 'package:courtly/core/widgets/app_text_field.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('renders the validation error below the input', (tester) async {
    const errorText = 'Enter a valid email address';

    await tester.pumpWidget(
      MaterialApp(
        theme: AppTheme.light,
        home: const Scaffold(
          body: AppTextField(
            label: 'Email',
            autovalidateMode: AutovalidateMode.always,
            validator: _alwaysInvalid,
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    // The error message is shown...
    expect(find.text(errorText), findsOneWidget);

    // ...and it sits BELOW the editable input line (rubric §4), never inside it.
    final inputBottom = tester.getBottomLeft(find.byType(EditableText)).dy;
    final errorTop = tester.getTopLeft(find.text(errorText)).dy;
    expect(errorTop, greaterThan(inputBottom));
  });
}

String? _alwaysInvalid(String? _) => 'Enter a valid email address';
