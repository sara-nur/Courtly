import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../core/theme/app_colors.dart';
import '../../core/theme/app_spacing.dart';
import 'courtly_logo.dart';

/// Admin desktop shell: a persistent top navigation bar (logo, primary tabs,
/// global search, notifications bell, profile menu) wrapping the routed body.
/// Layout mirrors `ui_design_and_scope.pdf` p.5.
class AdminShell extends StatelessWidget {
  const AdminShell({super.key, required this.navigationShell});

  /// Supplied by `StatefulShellRoute.indexedStack`; preserves each tab's state.
  final StatefulNavigationShell navigationShell;

  static const List<_AdminNavItem> _items = [
    _AdminNavItem('Dashboard', Icons.dashboard_outlined),
    _AdminNavItem('Reservations', Icons.event_note_outlined),
    _AdminNavItem('Courts', Icons.sports_tennis_outlined),
    _AdminNavItem('Users', Icons.people_outline),
    _AdminNavItem('Reports', Icons.bar_chart_outlined),
  ];

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: Column(
        children: [
          _TopNav(
            items: _items,
            currentIndex: navigationShell.currentIndex,
            onSelect: (index) => navigationShell.goBranch(
              index,
              initialLocation: index == navigationShell.currentIndex,
            ),
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
  const _AdminNavItem(this.label, this.icon);
  final String label;
  final IconData icon;
}

class _TopNav extends StatelessWidget {
  const _TopNav({
    required this.items,
    required this.currentIndex,
    required this.onSelect,
  });

  final List<_AdminNavItem> items;
  final int currentIndex;
  final ValueChanged<int> onSelect;

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
          for (var i = 0; i < items.length; i++)
            _NavTab(
              item: items[i],
              selected: i == currentIndex,
              onTap: () => onSelect(i),
            ),
          const Spacer(),
          const _GlobalSearch(),
          const SizedBox(width: AppSpacing.sm),
          IconButton(
            tooltip: 'Notifications',
            onPressed: () {},
            icon: const Icon(Icons.notifications_none),
            color: AppColors.textSecondary,
          ),
          const SizedBox(width: AppSpacing.xs),
          const _ProfileMenu(),
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

class _GlobalSearch extends StatelessWidget {
  const _GlobalSearch();

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: 240,
      child: TextField(
        decoration: InputDecoration(
          hintText: 'Search…',
          prefixIcon: const Icon(Icons.search, size: 18),
          isDense: true,
          fillColor: AppColors.surfaceMuted,
          contentPadding: const EdgeInsets.symmetric(
            horizontal: AppSpacing.sm,
            vertical: AppSpacing.xs,
          ),
        ),
      ),
    );
  }
}

class _ProfileMenu extends StatelessWidget {
  const _ProfileMenu();

  @override
  Widget build(BuildContext context) {
    return PopupMenuButton<String>(
      tooltip: 'Account',
      position: PopupMenuPosition.under,
      onSelected: (_) {},
      itemBuilder: (context) => const [
        PopupMenuItem(value: 'profile', child: Text('Profile')),
        PopupMenuItem(value: 'logout', child: Text('Sign out')),
      ],
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          const Column(
            crossAxisAlignment: CrossAxisAlignment.end,
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Text(
                'Becky Doe',
                style: TextStyle(
                  fontWeight: FontWeight.w600,
                  fontSize: 13,
                  color: AppColors.textPrimary,
                ),
              ),
              Text(
                'Super Admin',
                style: TextStyle(fontSize: 11, color: AppColors.textMuted),
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
