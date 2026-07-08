import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/constants/app_roles.dart';
import '../../core/theme/app_colors.dart';
import '../../core/theme/app_spacing.dart';
import '../../core/widgets/confirm_dialog.dart';
import '../../features/auth/application/auth_controller.dart';
import '../../features/auth/domain/auth_models.dart';
import 'courtly_logo.dart';

/// Admin desktop shell: a persistent top navigation bar (logo, primary tabs,
/// profile menu) wrapping the routed body. The profile menu shows the
/// signed-in user and signs out via the real auth API (Feature 8).
class AdminShell extends ConsumerWidget {
  const AdminShell({super.key, required this.navigationShell});

  /// Supplied by `StatefulShellRoute.indexedStack`; preserves each tab's state.
  final StatefulNavigationShell navigationShell;

  static const List<_AdminNavItem> _items = [
    _AdminNavItem('Dashboard', Icons.dashboard_outlined, adminOnly: true),
    _AdminNavItem('Reservations', Icons.event_note_outlined),
    _AdminNavItem('Courts', Icons.sports_tennis_outlined),
    _AdminNavItem('Users', Icons.people_outline),
    _AdminNavItem('Reports', Icons.bar_chart_outlined, adminOnly: true),
    _AdminNavItem('Settings', Icons.settings_outlined),
    _AdminNavItem('News', Icons.newspaper_outlined),
  ];

  Future<void> _confirmSignOut(BuildContext context, WidgetRef ref) async {
    final confirmed = await ConfirmDialog.show(
      context,
      title: 'Sign out',
      message: 'Sign out of Courtly Admin?',
      confirmLabel: 'Sign out',
      icon: Icons.logout,
    );
    if (confirmed) {
      await ref.read(authControllerProvider.notifier).logout();
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final user = ref.watch(authControllerProvider).user;
    final isAdmin = user?.hasAnyRole({AppRoles.admin}) ?? false;

    // Dashboard + Reports are Admin-only (enforced by the API). Hide them for
    // non-admins, keeping each visible tab's TRUE branch index so navigation and
    // the selected-tab highlight stay aligned with the shell's indexed stack.
    final entries = <({int branch, _AdminNavItem item})>[
      for (var i = 0; i < _items.length; i++)
        if (isAdmin || !_items[i].adminOnly) (branch: i, item: _items[i]),
    ];

    return Scaffold(
      body: Column(
        children: [
          _TopNav(
            entries: entries,
            currentIndex: navigationShell.currentIndex,
            user: user,
            onSelect: (index) => navigationShell.goBranch(
              index,
              initialLocation: index == navigationShell.currentIndex,
            ),
            onSignOut: () => _confirmSignOut(context, ref),
          ),
          Expanded(
            child: Align(
              alignment: Alignment.topCenter,
              child: ConstrainedBox(
                constraints:
                    const BoxConstraints(maxWidth: AppSpacing.contentMaxWidth),
                child: navigationShell,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _AdminNavItem {
  const _AdminNavItem(this.label, this.icon, {this.adminOnly = false});
  final String label;
  final IconData icon;

  /// Only shown to Admins (the API restricts the underlying endpoints to Admin).
  final bool adminOnly;
}

class _TopNav extends StatelessWidget {
  const _TopNav({
    required this.entries,
    required this.currentIndex,
    required this.user,
    required this.onSelect,
    required this.onSignOut,
  });

  /// Visible tabs, each carrying its true branch index in the shell's indexed stack.
  final List<({int branch, _AdminNavItem item})> entries;
  final int currentIndex;
  final AuthUser? user;
  final ValueChanged<int> onSelect;
  final VoidCallback onSignOut;

  @override
  Widget build(BuildContext context) {
    return Container(
      height: AppSpacing.topNavHeight,
      decoration: const BoxDecoration(
        color: AppColors.surface,
        border: Border(bottom: BorderSide(color: AppColors.border)),
      ),
      padding: const EdgeInsets.symmetric(horizontal: AppSpacing.lg),
      child: Row(
        children: [
          const CourtlyLogo(showAdminSuffix: true),
          const SizedBox(width: AppSpacing.xl),
          for (final entry in entries)
            _NavTab(
              item: entry.item,
              selected: entry.branch == currentIndex,
              onTap: () => onSelect(entry.branch),
            ),
          const Spacer(),
          _ProfileMenu(user: user, onSignOut: onSignOut),
        ],
      ),
    );
  }
}

class _NavTab extends StatelessWidget {
  const _NavTab({
    required this.item,
    required this.selected,
    required this.onTap,
  });

  final _AdminNavItem item;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: AppSpacing.xxs),
      child: InkWell(
        borderRadius: AppSpacing.brSm,
        onTap: onTap,
        child: Container(
          padding: const EdgeInsets.symmetric(
            horizontal: AppSpacing.sm,
            vertical: AppSpacing.xs,
          ),
          decoration: BoxDecoration(
            color: selected ? AppColors.primarySoft : Colors.transparent,
            borderRadius: AppSpacing.brSm,
          ),
          child: Text(
            item.label,
            style: TextStyle(
              fontWeight: FontWeight.w600,
              color: selected ? AppColors.primary : AppColors.textSecondary,
            ),
          ),
        ),
      ),
    );
  }
}

class _ProfileMenu extends StatelessWidget {
  const _ProfileMenu({required this.user, required this.onSignOut});

  final AuthUser? user;
  final VoidCallback onSignOut;

  static const String _signOut = 'sign-out';

  @override
  Widget build(BuildContext context) {
    final name = user?.displayName ?? '—';
    final role = user?.primaryRole ?? '';
    return PopupMenuButton<String>(
      tooltip: 'Account',
      position: PopupMenuPosition.under,
      onSelected: (value) {
        if (value == _signOut) onSignOut();
      },
      itemBuilder: (context) => const [
        PopupMenuItem(
          value: _signOut,
          child: ListTile(
            contentPadding: EdgeInsets.zero,
            leading: Icon(Icons.logout, size: 20),
            title: Text('Sign out'),
          ),
        ),
      ],
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Column(
            crossAxisAlignment: CrossAxisAlignment.end,
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Text(
                name,
                style: const TextStyle(
                  fontWeight: FontWeight.w600,
                  fontSize: 13,
                  color: AppColors.textPrimary,
                ),
              ),
              if (role.isNotEmpty)
                Text(
                  role,
                  style: const TextStyle(
                    fontSize: 11,
                    color: AppColors.textMuted,
                  ),
                ),
            ],
          ),
          const SizedBox(width: AppSpacing.xs),
          const CircleAvatar(
            radius: AppSpacing.avatarSm / 2,
            backgroundColor: AppColors.surfaceMuted,
            child: Icon(Icons.person, size: 18, color: AppColors.textSecondary),
          ),
        ],
      ),
    );
  }
}
