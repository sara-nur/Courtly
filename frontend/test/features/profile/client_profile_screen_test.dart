import 'dart:typed_data';

import 'package:courtly/core/theme/app_theme.dart';
import 'package:courtly/core/widgets/app_text_field.dart';
import 'package:courtly/features/auth/application/auth_controller.dart';
import 'package:courtly/features/auth/domain/auth_models.dart';
import 'package:courtly/features/court_catalog/application/court_providers.dart';
import 'package:courtly/features/profile/application/profile_providers.dart';
import 'package:courtly/features/profile/data/profile_repository.dart';
import 'package:courtly/features/profile/presentation/client_profile_screen.dart';
import 'package:courtly/features/reference_data/domain/reference_models.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  const user = AuthUser(
    id: 'u1',
    userName: 'ada',
    email: 'ada@example.com',
    firstName: 'Ada',
    lastName: 'Lovelace',
    roles: ['User'],
  );

  const sarajevo = City(id: 1, name: 'Sarajevo', countryId: 1, countryName: 'Bosnia');

  Future<_FakeProfileRepository> pumpProfile(WidgetTester tester) async {
    final repo = _FakeProfileRepository();
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          profileRepositoryProvider.overrideWithValue(repo),
          authControllerProvider.overrideWith(() => _FakeAuthController(user)),
          cityLookupProvider.overrideWith((ref) async => const [sarajevo]),
          avatarBytesProvider.overrideWith((ref) => null),
        ],
        child: MaterialApp(
          theme: AppTheme.light,
          home: const Scaffold(body: ClientProfileScreen()),
        ),
      ),
    );
    await tester.pumpAndSettle();
    return repo;
  }

  testWidgets(
      'password fields stay hidden until the toggle is on, and saving the '
      'profile never requires them', (tester) async {
    final repo = await pumpProfile(tester);

    // Toggle off: no password fields in the tree.
    expect(find.widgetWithText(AppTextField, 'Current password'), findsNothing);
    expect(find.widgetWithText(AppTextField, 'New password'), findsNothing);

    // Profile is prefilled + valid, so saving it validates only the profile
    // fields — no password-required message appears (rubric §288/§290).
    await tester.tap(find.widgetWithText(ElevatedButton, 'Save changes'));
    await tester.pumpAndSettle();

    expect(find.text('Current password is required.'), findsNothing);
    expect(repo.updateCalled, isTrue);
  });

  testWidgets('turning the toggle on and submitting empty shows a required '
      'message below each password field', (tester) async {
    await pumpProfile(tester);

    await tester.tap(find.byType(SwitchListTile));
    await tester.pumpAndSettle();

    // Fields now present.
    expect(find.widgetWithText(AppTextField, 'Current password'), findsOneWidget);

    await tester.tap(find.widgetWithText(ElevatedButton, 'Update password'));
    await tester.pumpAndSettle();

    expect(find.text('Current password is required.'), findsOneWidget);
    expect(find.text('Password is required.'), findsOneWidget); // new password
    expect(find.text('Please confirm your password.'), findsOneWidget);
  });

  testWidgets('a weak new password and a mismatched confirmation each show '
      'their own message below the field', (tester) async {
    await pumpProfile(tester);

    await tester.tap(find.byType(SwitchListTile));
    await tester.pumpAndSettle();

    await tester.enterText(
        find.widgetWithText(AppTextField, 'Current password'), 'Passw0rd!');
    await tester.enterText(
        find.widgetWithText(AppTextField, 'New password'), 'weak');
    await tester.enterText(
        find.widgetWithText(AppTextField, 'Confirm new password'), 'different');
    await tester.tap(find.widgetWithText(ElevatedButton, 'Update password'));
    await tester.pumpAndSettle();

    expect(
      find.text(
          'Password must be at least 8 characters and include an uppercase '
          'letter, a lowercase letter, and a number.'),
      findsOneWidget,
    );
    expect(find.text('Passwords do not match.'), findsOneWidget);
  });
}

/// Stand-in repository so the screen runs without a live API.
class _FakeProfileRepository implements ProfileRepository {
  bool updateCalled = false;

  @override
  Future<AuthUser> updateProfile({
    required String firstName,
    required String lastName,
    required String email,
    int? cityId,
  }) async {
    updateCalled = true;
    return AuthUser(
      id: 'u1',
      userName: 'ada',
      email: email,
      firstName: firstName,
      lastName: lastName,
      roles: const ['User'],
    );
  }

  @override
  Future<void> changePassword({
    required String currentPassword,
    required String newPassword,
  }) async {}

  @override
  Future<AuthUser> uploadAvatar({
    required List<int> bytes,
    required String filename,
  }) async =>
      throw UnimplementedError();

  @override
  Future<Uint8List?> fetchAvatarBytes() async => null;
}

/// Fake auth controller that reports a fixed authenticated user without
/// touching secure storage or the network on build.
class _FakeAuthController extends AuthController {
  _FakeAuthController(this._user);

  final AuthUser _user;

  @override
  AuthState build() => AuthState.authenticated(_user);
}
