// F27 — Client notifications UI. The paginated notifications list screen.
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../app/router/client_router.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../../../core/widgets/widgets.dart';
import '../../reference_data/domain/reference_models.dart' show PagedResult;
import '../application/notification_providers.dart';
import '../domain/notification_models.dart';
import 'widgets/notification_tile.dart';

/// The signed-in user's notifications, newest-first and paginated. Sits on the
/// bottom-nav shell's Notifications destination. The current page is held
/// locally and re-watches [notificationsPageProvider] as it changes; the
/// realtime controller's `revision` invalidates the visible page so a hub push
/// or a poll refreshes the list in place. Mirrors `ClientCourtReviewsScreen`.
class ClientNotificationsScreen extends ConsumerStatefulWidget {
  const ClientNotificationsScreen({super.key});

  @override
  ConsumerState<ClientNotificationsScreen> createState() =>
      _ClientNotificationsScreenState();
}

class _ClientNotificationsScreenState
    extends ConsumerState<ClientNotificationsScreen> {
  int _page = 1;

  @override
  Widget build(BuildContext context) {
    // A realtime change (hub push or poll) bumps `revision` — refresh the page.
    ref.listen<int>(
      notificationsRealtimeControllerProvider.select((s) => s.revision),
      (_, __) => ref.invalidate(notificationsPageProvider(_page)),
    );

    final unreadCount = ref.watch(
      notificationsRealtimeControllerProvider.select((s) => s.unreadCount),
    );
    final value = ref.watch(notificationsPageProvider(_page));

    return Scaffold(
      appBar: AppBar(
        title: const Text('Notifications'),
        actions: [
          IconButton(
            icon: const Icon(Icons.done_all),
            tooltip: 'Mark all read',
            onPressed: unreadCount > 0
                ? () async {
                    await ref
                        .read(notificationsRealtimeControllerProvider.notifier)
                        .markAllRead();
                    ref.invalidate(notificationsPageProvider(_page));
                  }
                : null,
          ),
        ],
      ),
      body: AsyncValueView<PagedResult<AppNotification>>(
        value: value,
        onRetry: () => ref.invalidate(notificationsPageProvider(_page)),
        data: (paged) => PaginatedListView<AppNotification>(
          items: paged.items,
          page: paged.page,
          pageSize: paged.pageSize,
          totalCount: paged.totalCount,
          hasNext: paged.hasNext,
          hasPrevious: paged.hasPrevious,
          onNextPage: () => setState(() => _page += 1),
          onPreviousPage: () => setState(() => _page -= 1),
          padding: const EdgeInsets.symmetric(vertical: AppSpacing.xs),
          separator: const Divider(height: 1),
          emptyPlaceholder: const _EmptyNotifications(),
          itemBuilder: (context, notification, _) => NotificationTile(
            notification: notification,
            // Tapping any row opens the full notification. If it was unread we
            // mark it read on the way in (fire-and-forget): that bumps the
            // controller's `revision`, and the `ref.listen` above invalidates
            // the visible page so the row's unread styling clears + the badge
            // updates when we return.
            onTap: () {
              if (!notification.isRead) {
                ref
                    .read(notificationsRealtimeControllerProvider.notifier)
                    .markRead(notification.id);
              }
              context.push(
                ClientRoutes.notificationDetail,
                extra: notification,
              );
            },
          ),
        ),
      ),
    );
  }
}

/// Friendly empty state shown when the user has no notifications.
class _EmptyNotifications extends StatelessWidget {
  const _EmptyNotifications();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(AppSpacing.xl),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(
              Icons.notifications_none,
              size: 48,
              color: AppColors.textMuted,
            ),
            const SizedBox(height: AppSpacing.sm),
            Text(
              "You're all caught up",
              style: theme.textTheme.titleMedium
                  ?.copyWith(color: AppColors.textSecondary),
            ),
            const SizedBox(height: AppSpacing.xxs),
            Text(
              'No notifications yet',
              style: theme.textTheme.bodyMedium
                  ?.copyWith(color: AppColors.textMuted),
            ),
          ],
        ),
      ),
    );
  }
}
