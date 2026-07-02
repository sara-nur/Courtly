import 'package:courtly/core/enums/reservation_status.dart';
import 'package:courtly/core/widgets/confirm_dialog.dart';
import 'package:courtly/features/auth/application/auth_controller.dart';
import 'package:courtly/features/auth/domain/auth_models.dart';
import 'package:courtly/features/reference_data/domain/reference_models.dart'
    show PagedResult;
import 'package:courtly/features/reservations/application/reservation_providers.dart';
import 'package:courtly/features/reservations/data/reservation_repository.dart';
import 'package:courtly/features/reservations/domain/reservation_models.dart';
import 'package:courtly/features/users/application/user_providers.dart';
import 'package:courtly/features/users/data/user_repository.dart';
import 'package:courtly/features/users/domain/user_detail.dart';
import 'package:courtly/features/users/domain/user_summary.dart';
import 'package:courtly/features/users/presentation/user_detail_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

/// Feature 15A DoD (widget): the detail renders the user's profile, and tapping
/// Deactivate → confirming calls the repository's `setActive(id, false)`. The
/// user's reservations section renders (empty) via the reservations repo.
void main() {
  UserDetail user({bool isActive = true, bool isProtected = false}) =>
      UserDetail(
        id: 'u-1',
        firstName: 'Alex',
        lastName: 'Johnson',
        fullName: 'Alex Johnson',
        email: 'alex@courtly.test',
        phoneNumber: '+1 555 0100',
        cityName: 'Paris',
        roles: const ['User'],
        isActive: isActive,
        createdAtUtc: DateTime.utc(2026, 1, 15, 12, 0),
        isProtected: isProtected,
      );

  Future<void> pumpDetail(
    WidgetTester tester,
    _FakeUserRepository repo,
  ) async {
    tester.view.physicalSize = const Size(1200, 1600);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          // Detail resolves through the repo's getById; overriding the repo is
          // enough (no family-element override needed).
          userRepositoryProvider.overrideWithValue(repo),
          // The reservations section reuses the reservations repo — empty page.
          reservationRepositoryProvider
              .overrideWithValue(_FakeReservationRepository()),
          // A signed-in admin whose id differs from the target, so the
          // self-deactivate guard stays off (Deactivate is enabled).
          authControllerProvider.overrideWith(_FakeAuthController.new),
        ],
        child: const MaterialApp(
          home: Scaffold(body: UserDetailScreen(userId: 'u-1')),
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('renders the user profile summary', (tester) async {
    await pumpDetail(tester, _FakeUserRepository(user()));

    expect(find.text('Alex Johnson'), findsWidgets);
    expect(find.text('alex@courtly.test'), findsOneWidget);
    expect(find.text('Paris'), findsOneWidget);
  });

  testWidgets('Deactivate action calls setActive(false) after the dialog',
      (tester) async {
    final repo = _FakeUserRepository(user());
    await pumpDetail(tester, repo);

    await tester.tap(find.widgetWithText(OutlinedButton, 'Deactivate'));
    await tester.pumpAndSettle();
    expect(find.byType(ConfirmDialog), findsOneWidget);

    await tester.tap(find.descendant(
        of: find.byType(ConfirmDialog),
        matching: find.widgetWithText(ElevatedButton, 'Deactivate')));
    await tester.pumpAndSettle();

    expect(repo.setActiveId, 'u-1');
    expect(repo.setActiveValue, false);
  });

  testWidgets('super admin cannot be deactivated or re-roled (disabled + reason)',
      (tester) async {
    final repo = _FakeUserRepository(user(isProtected: true));
    await pumpDetail(tester, repo);

    // Both protected actions are disabled-with-reason (rubric §6).
    expect(
        find.byTooltip("The super administrator account can't be deactivated."),
        findsOneWidget);
    expect(
        find.byTooltip("The super administrator's role can't be changed."),
        findsOneWidget);

    // Tapping the disabled Deactivate does nothing (DisabledAction ignores taps).
    await tester.tap(find.widgetWithText(OutlinedButton, 'Deactivate'),
        warnIfMissed: false);
    await tester.pumpAndSettle();
    expect(find.byType(ConfirmDialog), findsNothing);
    expect(repo.setActiveId, isNull);
  });
}

/// Minimal fake — getById feeds the detail; setActive records the call. Any other
/// method (search for the list refresh) returns an empty page; unused calls fail.
class _FakeUserRepository implements UserRepository {
  _FakeUserRepository(this._detail);

  final UserDetail _detail;
  String? setActiveId;
  bool? setActiveValue;

  @override
  Future<UserDetail> getById(String id) async => _detail;

  @override
  Future<UserDetail> setActive(String id, bool isActive) async {
    setActiveId = id;
    setActiveValue = isActive;
    return _detail;
  }

  @override
  Future<PagedResult<UserSummary>> search({
    int page = 1,
    int pageSize = 20,
    String? search,
  }) async =>
      const PagedResult<UserSummary>(
        items: [],
        page: 1,
        pageSize: 20,
        totalCount: 0,
        hasNext: false,
        hasPrevious: false,
      );

  @override
  dynamic noSuchMethod(Invocation invocation) =>
      throw UnsupportedError('Unexpected call: ${invocation.memberName}');
}

/// The reservations section reuses this repo; only `list` is exercised (empty).
class _FakeReservationRepository implements ReservationRepository {
  @override
  Future<PagedResult<Reservation>> list({
    int page = 1,
    int pageSize = 20,
    ReservationStatus? status,
    int? courtId,
    String? userId,
    DateTime? fromUtc,
    DateTime? toUtc,
  }) async =>
      PagedResult<Reservation>(
        items: const [],
        page: 1,
        pageSize: pageSize,
        totalCount: 0,
        hasNext: false,
        hasPrevious: false,
      );

  @override
  dynamic noSuchMethod(Invocation invocation) =>
      throw UnsupportedError('Unexpected call: ${invocation.memberName}');
}

/// A hermetic auth controller: a signed-in admin whose id differs from the
/// target user, so the self-deactivate guard is not triggered. Skips the real
/// controller's storage/network restore.
class _FakeAuthController extends AuthController {
  @override
  AuthState build() => const AuthState.authenticated(
        AuthUser(
          id: 'admin-1',
          userName: 'admin',
          email: 'admin@courtly.test',
          firstName: 'Ada',
          lastName: 'Min',
          roles: ['Admin'],
        ),
      );
}
