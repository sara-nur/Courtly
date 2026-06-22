import 'package:courtly/core/network/api_exception.dart';
import 'package:courtly/core/theme/app_theme.dart';
import 'package:courtly/core/widgets/app_text_field.dart';
import 'package:courtly/features/auth/application/auth_providers.dart';
import 'package:courtly/features/auth/data/auth_repository.dart';
import 'package:courtly/features/auth/domain/auth_models.dart';
import 'package:courtly/features/auth/presentation/login_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  Future<void> pumpLogin(WidgetTester tester, AuthRepository repo) async {
    await tester.pumpWidget(
      ProviderScope(
        overrides: [authRepositoryProvider.overrideWithValue(repo)],
        child: MaterialApp(
          theme: AppTheme.light,
          home: const AdminLoginScreen(),
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  Future<void> enterCredentials(WidgetTester tester) async {
    await tester.enterText(find.byType(AppTextField).at(0), 'desktop');
    await tester.enterText(find.byType(AppTextField).at(1), 'secret');
  }

  testWidgets('shows required validation below each field on empty submit',
      (tester) async {
    await pumpLogin(tester, _FakeAuthRepository());

    await tester.tap(find.widgetWithText(ElevatedButton, 'Sign in'));
    await tester.pumpAndSettle();

    expect(find.text('Username or email is required.'), findsOneWidget);
    expect(find.text('Password is required.'), findsOneWidget);
  });

  testWidgets('shows the backend message as a banner on bad credentials',
      (tester) async {
    await pumpLogin(
      tester,
      _FakeAuthRepository(
        onLogin: (_, __) => throw const ApiException(
          message: 'Invalid credentials.',
          statusCode: 401,
        ),
      ),
    );

    await enterCredentials(tester);
    await tester.tap(find.widgetWithText(ElevatedButton, 'Sign in'));
    await tester.pumpAndSettle();

    expect(find.text('Invalid credentials.'), findsOneWidget);
  });

  testWidgets('surfaces backend validation messages below their fields',
      (tester) async {
    await pumpLogin(
      tester,
      _FakeAuthRepository(
        onLogin: (_, __) => throw const ApiException(
          message: 'Validation failed',
          statusCode: 400,
          fieldErrors: {
            'usernameoremail': ['No account matches that username.'],
            'password': ['Incorrect password.'],
          },
        ),
      ),
    );

    await enterCredentials(tester);
    await tester.tap(find.widgetWithText(ElevatedButton, 'Sign in'));
    await tester.pumpAndSettle();

    expect(find.text('No account matches that username.'), findsOneWidget);
    expect(find.text('Incorrect password.'), findsOneWidget);
  });
}

/// Stand-in for the repository so the form can be tested without a live API.
class _FakeAuthRepository implements AuthRepository {
  _FakeAuthRepository({this.onLogin});

  final Future<AuthUser> Function(String userNameOrEmail, String password)?
      onLogin;

  @override
  Future<AuthUser> login(String userNameOrEmail, String password) =>
      onLogin!(userNameOrEmail, password);

  @override
  Future<AuthUser?> restore() async => null;

  @override
  Future<void> logout() async {}
}
