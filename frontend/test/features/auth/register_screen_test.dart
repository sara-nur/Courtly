import 'package:courtly/core/theme/app_theme.dart';
import 'package:courtly/core/widgets/app_text_field.dart';
import 'package:courtly/features/auth/application/auth_providers.dart';
import 'package:courtly/features/auth/data/auth_repository.dart';
import 'package:courtly/features/auth/domain/auth_models.dart';
import 'package:courtly/features/auth/presentation/register_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  Future<void> pumpRegister(WidgetTester tester) async {
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          authRepositoryProvider.overrideWithValue(_FakeAuthRepository()),
        ],
        child: MaterialApp(
          theme: AppTheme.light,
          home: const RegisterScreen(),
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('has no role field — only name/email/password inputs',
      (tester) async {
    await pumpRegister(tester);

    // The register form never lets a client pick a role (rubric §5): no dropdown
    // of any kind, and the five inputs are first/last/email/password/confirm.
    expect(find.byType(DropdownButtonFormField), findsNothing);
    expect(find.textContaining('Role'), findsNothing);
    expect(find.byType(AppTextField), findsNWidgets(5));
  });

  testWidgets('shows required validation below each field on empty submit',
      (tester) async {
    await pumpRegister(tester);

    await tester.tap(find.widgetWithText(ElevatedButton, 'Create account'));
    await tester.pumpAndSettle();

    expect(find.text('First name is required.'), findsOneWidget);
    expect(find.text('Last name is required.'), findsOneWidget);
    expect(find.text('Email is required.'), findsOneWidget);
    expect(find.text('Password is required.'), findsOneWidget);
    expect(find.text('Please confirm your password.'), findsOneWidget);
  });

  testWidgets('rejects an invalid email format', (tester) async {
    await pumpRegister(tester);

    await tester.enterText(find.byType(AppTextField).at(2), 'not-an-email');
    await tester.tap(find.widgetWithText(ElevatedButton, 'Create account'));
    await tester.pumpAndSettle();

    expect(
      find.text('Enter a valid email address (e.g. name@example.com).'),
      findsOneWidget,
    );
  });

  testWidgets('rejects mismatched password confirmation', (tester) async {
    await pumpRegister(tester);

    await tester.enterText(find.byType(AppTextField).at(0), 'Ada');
    await tester.enterText(find.byType(AppTextField).at(1), 'Lovelace');
    await tester.enterText(find.byType(AppTextField).at(2), 'ada@example.com');
    await tester.enterText(find.byType(AppTextField).at(3), 'Password1');
    await tester.enterText(find.byType(AppTextField).at(4), 'Password2');
    await tester.tap(find.widgetWithText(ElevatedButton, 'Create account'));
    await tester.pumpAndSettle();

    expect(find.text('Passwords do not match.'), findsOneWidget);
  });
}

/// Stand-in repository so the form can be tested without a live API.
class _FakeAuthRepository implements AuthRepository {
  @override
  Future<AuthUser> register({
    required String email,
    required String password,
    required String firstName,
    required String lastName,
    int? cityId,
  }) async =>
      const AuthUser(
        id: '1',
        userName: 'ada',
        email: 'ada@example.com',
        firstName: 'Ada',
        lastName: 'Lovelace',
        roles: ['User'],
      );

  @override
  Future<AuthUser> login(String userNameOrEmail, String password) =>
      throw UnimplementedError();

  @override
  Future<void> forgotPassword(String email) async {}

  @override
  Future<AuthUser?> restore() async => null;

  @override
  Future<void> logout() async {}
}
