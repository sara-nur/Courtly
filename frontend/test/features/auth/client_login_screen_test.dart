import 'package:courtly/core/network/api_exception.dart';
import 'package:courtly/core/theme/app_theme.dart';
import 'package:courtly/core/widgets/app_text_field.dart';
import 'package:courtly/features/auth/application/auth_providers.dart';
import 'package:courtly/features/auth/data/auth_repository.dart';
import 'package:courtly/features/auth/domain/auth_models.dart';
import 'package:courtly/features/auth/presentation/client_login_screen.dart';
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
          home: const ClientLoginScreen(),
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('offers register and forgot-password entry points',
      (tester) async {
    await pumpLogin(tester, _FakeAuthRepository());

    expect(find.text('Create account'), findsOneWidget);
    expect(find.text('Forgot password?'), findsOneWidget);
  });

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

    await tester.enterText(find.byType(AppTextField).at(0), 'mobile');
    await tester.enterText(find.byType(AppTextField).at(1), 'secret');
    await tester.tap(find.widgetWithText(ElevatedButton, 'Sign in'));
    await tester.pumpAndSettle();

    expect(find.text('Invalid credentials.'), findsOneWidget);
  });
}

/// Stand-in repository so the form can be tested without a live API.
class _FakeAuthRepository implements AuthRepository {
  _FakeAuthRepository({this.onLogin});

  final Future<AuthUser> Function(String userNameOrEmail, String password)?
      onLogin;

  @override
  Future<AuthUser> login(String userNameOrEmail, String password) =>
      onLogin!(userNameOrEmail, password);

  @override
  Future<AuthUser> register({
    required String email,
    required String password,
    required String firstName,
    required String lastName,
    int? cityId,
  }) =>
      throw UnimplementedError();

  @override
  Future<void> forgotPassword(String email) async {}

  @override
  Future<AuthUser?> restore() async => null;

  @override
  Future<void> logout() async {}
}
