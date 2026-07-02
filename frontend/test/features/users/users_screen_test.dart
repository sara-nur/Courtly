import 'package:courtly/core/widgets/status_badge.dart';
import 'package:courtly/features/reference_data/domain/reference_models.dart'
    show PagedResult;
import 'package:courtly/features/users/application/user_providers.dart';
import 'package:courtly/features/users/data/user_repository.dart';
import 'package:courtly/features/users/domain/user_summary.dart';
import 'package:courtly/features/users/presentation/users_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

/// Feature 15A DoD (widget): the users table renders rows (name, email, role +
/// active badge) from the repository and shows the paged footer; the search box
/// drives the backend `search` filter.
void main() {
  const user = UserSummary(
    id: 'u-1',
    fullName: 'Alex Johnson',
    email: 'alex@courtly.test',
    role: 'Admin',
    isActive: true,
  );

  Future<void> pumpScreen(WidgetTester tester, List<UserSummary> rows) async {
    tester.view.physicalSize = const Size(1200, 1600);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          userRepositoryProvider.overrideWithValue(_FakeUserRepository(rows)),
        ],
        child: const MaterialApp(home: Scaffold(body: UsersScreen())),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('renders a user row with name, email, role and active badge',
      (tester) async {
    await pumpScreen(tester, [user]);

    expect(find.text('Alex Johnson'), findsOneWidget);
    expect(find.text('alex@courtly.test'), findsOneWidget);
    // The role renders as a badge (not just text), proving the role→tone map.
    expect(
      find.descendant(of: find.byType(StatusBadge), matching: find.text('Admin')),
      findsOneWidget,
    );
    // Active state renders as a badge too.
    expect(
      find.descendant(
          of: find.byType(StatusBadge), matching: find.text('Active')),
      findsOneWidget,
    );
    // Paged footer (dash glyph is the widget's; match on the stable suffix).
    expect(find.textContaining('Showing'), findsOneWidget);
    expect(find.textContaining('of 1'), findsOneWidget);
  });

  testWidgets('shows the empty placeholder when there are no users',
      (tester) async {
    await pumpScreen(tester, const []);

    expect(find.text('No users match this search'), findsOneWidget);
  });

  testWidgets('typing in the search box passes the term to the repository',
      (tester) async {
    final repo = _FakeUserRepository([user]);
    tester.view.physicalSize = const Size(1200, 1600);
    tester.view.devicePixelRatio = 1.0;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      ProviderScope(
        overrides: [userRepositoryProvider.overrideWithValue(repo)],
        child: const MaterialApp(home: Scaffold(body: UsersScreen())),
      ),
    );
    await tester.pumpAndSettle();

    await tester.enterText(find.byType(TextFormField), 'alex');
    await tester.pumpAndSettle();

    expect(repo.lastSearch, 'alex');
  });
}

/// Minimal fake — only `search` is exercised by the table; any other call is an
/// explicit test failure (mirrors the reservations screen test's fake pattern).
class _FakeUserRepository implements UserRepository {
  _FakeUserRepository(this.rows);

  final List<UserSummary> rows;
  String? lastSearch;

  @override
  Future<PagedResult<UserSummary>> search({
    int page = 1,
    int pageSize = 20,
    String? search,
  }) async {
    lastSearch = search;
    return PagedResult<UserSummary>(
      items: rows,
      page: 1,
      pageSize: pageSize,
      totalCount: rows.length,
      hasNext: false,
      hasPrevious: false,
    );
  }

  @override
  dynamic noSuchMethod(Invocation invocation) =>
      throw UnsupportedError('Unexpected call: ${invocation.memberName}');
}
