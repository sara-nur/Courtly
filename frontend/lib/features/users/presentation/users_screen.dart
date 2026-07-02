import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../app/router/admin_router.dart';
import '../../../core/constants/app_roles.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../../../core/widgets/app_text_field.dart';
import '../../../core/widgets/async_value_view.dart';
import '../../../core/widgets/paginated_list_view.dart';
import '../../../core/widgets/status_badge.dart';
import '../../reference_data/domain/reference_models.dart' show PagedResult;
import '../application/user_providers.dart';
import '../domain/user_summary.dart';

/// User Management (Feature 15A): a searchable table of every user (name, email,
/// role, active state) that links to the master-detail. The search box is
/// applied at the backend (name/email term); each row opens the detail where an
/// admin can activate/deactivate and assign a role. No raw ids are shown.
class UsersScreen extends ConsumerWidget {
  const UsersScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final state = ref.watch(userListControllerProvider);
    final controller = ref.read(userListControllerProvider.notifier);

    return Padding(
      padding: AppSpacing.pagePadding,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text('User Management', style: theme.textTheme.headlineSmall),
              const SizedBox(height: AppSpacing.xxs),
              Text(
                'Browse users and their reservations, and manage access.',
                style: theme.textTheme.bodyMedium,
              ),
            ],
          ),
          const SizedBox(height: AppSpacing.md),
          _SearchBar(controller: controller),
          const SizedBox(height: AppSpacing.md),
          const _TableHeader(),
          Expanded(
            child: AsyncValueView<PagedResult<UserSummary>>(
              value: state.value,
              onRetry: controller.load,
              data: (result) => PaginatedListView<UserSummary>(
                items: result.items,
                page: result.page,
                pageSize: result.pageSize,
                totalCount: result.totalCount,
                hasNext: result.hasNext,
                hasPrevious: result.hasPrevious,
                onNextPage: controller.nextPage,
                onPreviousPage: controller.prevPage,
                separator: const Divider(height: 1),
                emptyPlaceholder: const _EmptyPlaceholder(),
                itemBuilder: (context, user, _) => _UserRow(
                  user: user,
                  onOpen: () => context.go(AdminRoutes.userDetail(user.id)),
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// A search field that drives the controller's backend `search` filter (name or
/// email). Blank clears the filter.
class _SearchBar extends StatelessWidget {
  const _SearchBar({required this.controller});

  final UserListController controller;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: 320,
      child: AppTextField(
        label: 'Search users',
        hint: 'Name or email',
        prefixIcon: Icons.search,
        onChanged: controller.setSearch,
        textInputAction: TextInputAction.search,
      ),
    );
  }
}

/// The table's column header (aligned with [_UserRow]'s flex weights).
class _TableHeader extends StatelessWidget {
  const _TableHeader();

  @override
  Widget build(BuildContext context) {
    final style = Theme.of(context)
        .textTheme
        .labelMedium
        ?.copyWith(color: AppColors.textMuted);
    return Padding(
      padding: const EdgeInsets.symmetric(
          horizontal: AppSpacing.sm, vertical: AppSpacing.xs),
      child: Row(
        children: [
          Expanded(flex: 6, child: Text('USER', style: style)),
          Expanded(flex: 3, child: Text('ROLE', style: style)),
          Expanded(flex: 3, child: Text('STATUS', style: style)),
          const SizedBox(width: 48, child: Text('')),
        ],
      ),
    );
  }
}

class _UserRow extends StatelessWidget {
  const _UserRow({required this.user, required this.onOpen});

  final UserSummary user;
  final VoidCallback onOpen;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final u = user;

    return InkWell(
      onTap: onOpen,
      child: Padding(
        padding: const EdgeInsets.symmetric(
            horizontal: AppSpacing.sm, vertical: AppSpacing.sm),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.center,
          children: [
            Expanded(
              flex: 6,
              child: Row(
                children: [
                  CircleAvatar(
                    radius: AppSpacing.avatarSm / 2,
                    backgroundColor: AppColors.primarySoft,
                    child: Text(
                      _initials(u.fullName),
                      style: const TextStyle(
                          fontSize: 12,
                          fontWeight: FontWeight.w600,
                          color: AppColors.primary),
                    ),
                  ),
                  const SizedBox(width: AppSpacing.sm),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(u.fullName,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: theme.textTheme.bodyMedium
                                ?.copyWith(fontWeight: FontWeight.w600)),
                        if (u.email != null && u.email!.isNotEmpty)
                          Text(u.email!,
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                              style: theme.textTheme.bodySmall
                                  ?.copyWith(color: AppColors.textMuted)),
                      ],
                    ),
                  ),
                ],
              ),
            ),
            Expanded(
              flex: 3,
              child: Align(
                alignment: Alignment.centerLeft,
                child: StatusBadge(
                  label: u.role ?? AppRoles.user,
                  tone: roleTone(u.role),
                ),
              ),
            ),
            Expanded(
              flex: 3,
              child: Align(
                alignment: Alignment.centerLeft,
                child: StatusBadge(
                  label: u.isActive ? 'Active' : 'Inactive',
                  tone: u.isActive ? StatusTone.success : StatusTone.neutral,
                ),
              ),
            ),
            SizedBox(
              width: 48,
              child: PopupMenuButton<String>(
                icon: const Icon(Icons.more_vert),
                tooltip: 'Actions',
                onSelected: (_) => onOpen(),
                itemBuilder: (context) => const [
                  PopupMenuItem<String>(
                    value: 'view',
                    child: Text('View details'),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  String _initials(String name) {
    final parts = name.trim().split(RegExp(r'\s+')).where((p) => p.isNotEmpty);
    if (parts.isEmpty) return '?';
    final letters = parts.take(2).map((p) => p[0].toUpperCase()).join();
    return letters.isEmpty ? '?' : letters;
  }
}

/// Maps a role name to its badge tone (Admin → info, Staff → warning, otherwise
/// neutral). Shared with the detail screen so the coloring stays consistent.
StatusTone roleTone(String? role) {
  switch (role) {
    case AppRoles.admin:
      return StatusTone.info;
    case AppRoles.staff:
      return StatusTone.warning;
    default:
      return StatusTone.neutral;
  }
}

class _EmptyPlaceholder extends StatelessWidget {
  const _EmptyPlaceholder();

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          const Icon(Icons.person_search_outlined, color: AppColors.textMuted),
          const SizedBox(height: AppSpacing.xs),
          Text(
            'No users match this search',
            style: Theme.of(context)
                .textTheme
                .bodyMedium
                ?.copyWith(color: AppColors.textSecondary),
          ),
        ],
      ),
    );
  }
}
