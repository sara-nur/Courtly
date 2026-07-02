import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../app/router/admin_router.dart';
import '../../../core/constants/app_roles.dart';
import '../../../core/network/api_exception.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../../../core/utils/formatters.dart';
import '../../../core/widgets/app_back_button.dart';
import '../../../core/widgets/async_value_view.dart';
import '../../../core/widgets/confirm_dialog.dart';
import '../../../core/widgets/db_dropdown.dart';
import '../../../core/widgets/disabled_action.dart';
import '../../../core/widgets/paginated_list_view.dart';
import '../../../core/widgets/status_badge.dart';
import '../../auth/application/auth_controller.dart';
import '../../reference_data/domain/reference_models.dart' show PagedResult;
import '../../reservations/application/reservation_providers.dart';
import '../../reservations/domain/reservation_models.dart';
import '../application/user_providers.dart';
import '../domain/user_detail.dart';
import 'users_screen.dart' show roleTone;

/// Page size for a user's reservations preview on the detail screen (single
/// source — no magic numbers).
const int _kUserReservationsPageSize = 5;

/// A user's reservations, keyed by user id + page. Reuses the reservations repo
/// (`list(userId:)`), so the detail screen renders the same read-only rows the
/// reservations feature shows. Wrapped in a record key so pagination re-queries.
final _userReservationsProvider = FutureProvider.family<PagedResult<Reservation>,
    ({String userId, int page})>((ref, key) async {
  return ref.watch(reservationRepositoryProvider).list(
        userId: key.userId,
        page: key.page,
        pageSize: _kUserReservationsPageSize,
      );
});

/// User detail (Feature 15A master-detail): the full view of one user —
/// profile, roles, active state — plus the admin actions (activate/deactivate,
/// assign role) and a read-only list of the user's reservations. Invalid actions
/// are disabled-with-reason (rubric §6); an admin can't deactivate their own
/// account. After any action the detail + the table refresh automatically. No
/// raw ids are shown.
class UserDetailScreen extends ConsumerWidget {
  const UserDetailScreen({super.key, required this.userId});

  final String userId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final detail = ref.watch(userDetailProvider(userId));

    return Padding(
      padding: AppSpacing.pagePadding,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Align(
            alignment: Alignment.centerLeft,
            child: AppBackButton(
              label: 'Back to users',
              onPressed: () => context.go(AdminRoutes.users),
            ),
          ),
          const SizedBox(height: AppSpacing.sm),
          Expanded(
            child: AsyncValueView<UserDetail>(
              value: detail,
              onRetry: () => ref.invalidate(userDetailProvider(userId)),
              data: (d) => _DetailBody(detail: d),
            ),
          ),
        ],
      ),
    );
  }
}

class _DetailBody extends ConsumerWidget {
  const _DetailBody({required this.detail});

  final UserDetail detail;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final u = detail;

    return SingleChildScrollView(
      child: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 760),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Row(
                children: [
                  Text(u.fullName, style: theme.textTheme.headlineSmall),
                  const SizedBox(width: AppSpacing.sm),
                  StatusBadge(
                    label: u.isActive ? 'Active' : 'Inactive',
                    tone: u.isActive ? StatusTone.success : StatusTone.neutral,
                  ),
                ],
              ),
              const SizedBox(height: AppSpacing.md),
              _ActionsBar(detail: u),
              const SizedBox(height: AppSpacing.md),
              _SummaryCard(user: u),
              const SizedBox(height: AppSpacing.md),
              _ReservationsSection(userId: u.id),
            ],
          ),
        ),
      ),
    );
  }
}

/// A titled card holding a two-column label/value grid (rubric §6: aligned
/// label/value layout, not free-floating text).
class _Card extends StatelessWidget {
  const _Card({required this.title, required this.child});

  final String title;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Card(
      child: Padding(
        padding: AppSpacing.cardPadding,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(title, style: theme.textTheme.titleMedium),
            const SizedBox(height: AppSpacing.sm),
            child,
          ],
        ),
      ),
    );
  }
}

class _Row extends StatelessWidget {
  const _Row(this.label, this.value);

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.xxs),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(
            width: 160,
            child: Text(label,
                style: theme.textTheme.bodyMedium
                    ?.copyWith(color: AppColors.textSecondary)),
          ),
          Expanded(
            child: Text(value,
                style: theme.textTheme.bodyMedium
                    ?.copyWith(fontWeight: FontWeight.w600)),
          ),
        ],
      ),
    );
  }
}

class _SummaryCard extends StatelessWidget {
  const _SummaryCard({required this.user});

  final UserDetail user;

  @override
  Widget build(BuildContext context) {
    final u = user;
    return _Card(
      title: 'Profile',
      child: Column(
        children: [
          _Row('Name', u.fullName),
          if (u.email != null && u.email!.isNotEmpty) _Row('Email', u.email!),
          if (u.phoneNumber != null && u.phoneNumber!.isNotEmpty)
            _Row('Phone', u.phoneNumber!),
          if (u.cityName != null && u.cityName!.isNotEmpty)
            _Row('City', u.cityName!),
          Padding(
            padding: const EdgeInsets.symmetric(vertical: AppSpacing.xxs),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                SizedBox(
                  width: 160,
                  child: Text('Roles',
                      style: Theme.of(context)
                          .textTheme
                          .bodyMedium
                          ?.copyWith(color: AppColors.textSecondary)),
                ),
                Expanded(
                  child: Wrap(
                    spacing: AppSpacing.xs,
                    runSpacing: AppSpacing.xs,
                    children: [
                      if (u.roles.isEmpty)
                        StatusBadge(
                            label: AppRoles.user, tone: roleTone(AppRoles.user))
                      else
                        for (final role in u.roles)
                          StatusBadge(label: role, tone: roleTone(role)),
                    ],
                  ),
                ),
              ],
            ),
          ),
          _Row('Status', u.isActive ? 'Active' : 'Inactive'),
          _Row('Member since', Formatters.date(u.createdAtUtc.toLocal())),
        ],
      ),
    );
  }
}

/// The read-only "Reservations" section: this user's bookings, reusing the
/// reservations repository + model. Kept read-only here (management lives in the
/// reservations feature).
class _ReservationsSection extends ConsumerStatefulWidget {
  const _ReservationsSection({required this.userId});

  final String userId;

  @override
  ConsumerState<_ReservationsSection> createState() =>
      _ReservationsSectionState();
}

class _ReservationsSectionState extends ConsumerState<_ReservationsSection> {
  int _page = 1;

  @override
  Widget build(BuildContext context) {
    final value = ref.watch(_userReservationsProvider(
        (userId: widget.userId, page: _page)));

    return _Card(
      title: 'Reservations',
      child: SizedBox(
        height: 320,
        child: AsyncValueView<PagedResult<Reservation>>(
          value: value,
          onRetry: () => ref.invalidate(_userReservationsProvider(
              (userId: widget.userId, page: _page))),
          data: (result) => PaginatedListView<Reservation>(
            items: result.items,
            page: result.page,
            pageSize: result.pageSize,
            totalCount: result.totalCount,
            hasNext: result.hasNext,
            hasPrevious: result.hasPrevious,
            onNextPage: () => setState(() => _page++),
            onPreviousPage: () => setState(() => _page--),
            separator: const Divider(height: 1),
            emptyPlaceholder: const _NoReservations(),
            itemBuilder: (context, reservation, _) =>
                _ReservationRow(reservation: reservation),
          ),
        ),
      ),
    );
  }
}

class _ReservationRow extends StatelessWidget {
  const _ReservationRow({required this.reservation});

  final Reservation reservation;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final r = reservation;
    final start = r.slotStartUtc.toLocal();
    final end = r.slotEndUtc.toLocal();

    return Padding(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.sm),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.center,
        children: [
          Expanded(
            flex: 3,
            child: Text(r.reference,
                style: theme.textTheme.bodyMedium
                    ?.copyWith(fontWeight: FontWeight.w600)),
          ),
          Expanded(
            flex: 5,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(Formatters.date(start), style: theme.textTheme.bodyMedium),
                Text('${Formatters.time(start)} – ${Formatters.time(end)}',
                    style: theme.textTheme.bodySmall
                        ?.copyWith(color: AppColors.textMuted)),
              ],
            ),
          ),
          Expanded(
            flex: 3,
            child: Text(r.courtName,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: theme.textTheme.bodyMedium),
          ),
          Expanded(
            flex: 3,
            child: Align(
              alignment: Alignment.centerLeft,
              child: StatusBadge(label: r.status.label, tone: r.status.tone),
            ),
          ),
        ],
      ),
    );
  }
}

class _NoReservations extends StatelessWidget {
  const _NoReservations();

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Text(
        'This user has no reservations yet',
        style: Theme.of(context)
            .textTheme
            .bodyMedium
            ?.copyWith(color: AppColors.textSecondary),
      ),
    );
  }
}

/// The admin action bar: activate/deactivate (destructive confirm to deactivate)
/// and assign role (dropdown + Apply). Each action is disabled-with-reason when
/// it would be a no-op or is blocked (self-deactivate). After any action the
/// detail + the users table refresh automatically.
class _ActionsBar extends ConsumerStatefulWidget {
  const _ActionsBar({required this.detail});

  final UserDetail detail;

  @override
  ConsumerState<_ActionsBar> createState() => _ActionsBarState();
}

class _ActionsBarState extends ConsumerState<_ActionsBar> {
  /// Locally selected role for the "Assign role" dropdown, defaulting to the
  /// user's current primary role. Apply is disabled until it differs.
  late String _selectedRole = _currentRole;

  static const List<String> _roleOptions = [
    AppRoles.admin,
    AppRoles.staff,
    AppRoles.user,
  ];

  String get _currentRole {
    final roles = widget.detail.roles;
    if (roles.contains(AppRoles.admin)) return AppRoles.admin;
    if (roles.contains(AppRoles.staff)) return AppRoles.staff;
    return roles.isNotEmpty ? roles.first : AppRoles.user;
  }

  @override
  void didUpdateWidget(covariant _ActionsBar oldWidget) {
    super.didUpdateWidget(oldWidget);
    // Re-sync the dropdown to the server truth after a successful role change.
    if (oldWidget.detail.roles.join(',') != widget.detail.roles.join(',')) {
      _selectedRole = _currentRole;
    }
  }

  @override
  Widget build(BuildContext context) {
    final u = widget.detail;
    final currentUserId = ref.watch(authControllerProvider).user?.id;
    final isSelf = currentUserId != null && currentUserId == u.id;

    final activateReason =
        u.isActive ? 'This user is already active.' : null;
    final deactivateReason = u.isProtected
        ? 'The super administrator account can\'t be deactivated.'
        : !u.isActive
            ? 'This user is already inactive.'
            : (isSelf ? 'You can\'t deactivate your own account.' : null);
    final roleReason = u.isProtected
        ? 'The super administrator\'s role can\'t be changed.'
        : _selectedRole == _currentRole
            ? 'Pick a different role to apply.'
            : null;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Wrap(
          spacing: AppSpacing.sm,
          runSpacing: AppSpacing.sm,
          children: [
            DisabledAction(
              enabled: activateReason == null,
              reason: activateReason,
              child: ElevatedButton.icon(
                onPressed: () => _setActive(true),
                icon: const Icon(Icons.check_circle_outline, size: 18),
                label: const Text('Activate'),
              ),
            ),
            DisabledAction(
              enabled: deactivateReason == null,
              reason: deactivateReason,
              child: OutlinedButton.icon(
                onPressed: () => _setActive(false),
                style: OutlinedButton.styleFrom(
                    foregroundColor: AppColors.danger),
                icon: const Icon(Icons.block, size: 18),
                label: const Text('Deactivate'),
              ),
            ),
          ],
        ),
        const SizedBox(height: AppSpacing.md),
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            SizedBox(
              width: 240,
              child: DbDropdown<String>(
                label: 'Role',
                value: _selectedRole,
                items: _roleOptions,
                itemLabel: (r) => r,
                enabled: !u.isProtected,
                onChanged: (r) {
                  if (r != null) setState(() => _selectedRole = r);
                },
              ),
            ),
            const SizedBox(width: AppSpacing.sm),
            Padding(
              padding: const EdgeInsets.only(top: AppSpacing.xs),
              child: DisabledAction(
                enabled: roleReason == null,
                reason: roleReason,
                child: ElevatedButton.icon(
                  onPressed: _assignRole,
                  icon: const Icon(Icons.save, size: 18),
                  label: const Text('Apply'),
                ),
              ),
            ),
          ],
        ),
      ],
    );
  }

  Future<void> _setActive(bool isActive) async {
    final u = widget.detail;
    final ok = await ConfirmDialog.show(
      context,
      title: isActive ? 'Activate user' : 'Deactivate user',
      message: isActive
          ? 'Activate ${u.fullName}? They will regain access.'
          : 'Deactivate ${u.fullName}? They will lose access until reactivated.',
      confirmLabel: isActive ? 'Activate' : 'Deactivate',
      destructive: !isActive,
      icon: isActive ? Icons.check_circle_outline : Icons.block,
    );
    if (!ok || !mounted) return;
    await _run(
      () => ref.read(userRepositoryProvider).setActive(u.id, isActive),
      isActive ? '${u.fullName} activated.' : '${u.fullName} deactivated.',
    );
  }

  Future<void> _assignRole() async {
    final u = widget.detail;
    final role = _selectedRole;
    final ok = await ConfirmDialog.show(
      context,
      title: 'Assign role',
      message: 'Set ${u.fullName}\'s role to $role?',
      confirmLabel: 'Assign',
      icon: Icons.badge_outlined,
    );
    if (!ok || !mounted) return;
    await _run(
      () => ref.read(userRepositoryProvider).assignRole(u.id, role),
      '${u.fullName}\'s role set to $role.',
    );
  }

  /// Runs an action: refreshes the detail + the table on success, surfaces the
  /// backend message on failure (rubric §4 — never a generic error).
  Future<void> _run(
    Future<UserDetail> Function() action,
    String successMessage,
  ) async {
    try {
      await action();
      ref.invalidate(userDetailProvider(widget.detail.id));
      await ref.read(userListControllerProvider.notifier).refresh();
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text(successMessage)));
      }
    } on ApiException catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(e.message), backgroundColor: AppColors.danger),
        );
      }
    } catch (_) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text('Something went wrong. Please try again.'),
            backgroundColor: AppColors.danger,
          ),
        );
      }
    }
  }
}
