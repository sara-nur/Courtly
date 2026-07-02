import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../app/router/client_router.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../../../core/widgets/async_value_view.dart';
import '../../../core/widgets/paginated_list_view.dart';
import '../../reference_data/domain/reference_models.dart' show PagedResult;
import '../application/client_bookings_controller.dart';
import '../domain/reservation_models.dart';
import 'widgets/booking_summary_tile.dart';

/// The **Bookings** bottom-nav tab (F26A): the signed-in customer's own
/// reservations, split into **Upcoming** and **Past** by slot start. Each tab is
/// a paginated, server-ordered list of [BookingSummaryTile]s that deep-links to
/// the booking detail/receipt. Renders as a bare body — the client shell supplies
/// the Scaffold, app bar, and bottom nav.
class ClientBookingsScreen extends ConsumerWidget {
  const ClientBookingsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final state = ref.watch(clientBookingsControllerProvider);
    final controller = ref.read(clientBookingsControllerProvider.notifier);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(
            AppSpacing.lg,
            AppSpacing.lg,
            AppSpacing.lg,
            AppSpacing.sm,
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                'My Bookings',
                style: theme.textTheme.headlineSmall
                    ?.copyWith(fontWeight: FontWeight.w800),
              ),
              const SizedBox(height: AppSpacing.xxs),
              Text(
                'Your upcoming and past court reservations.',
                style: theme.textTheme.bodyMedium
                    ?.copyWith(color: AppColors.textSecondary),
              ),
            ],
          ),
        ),
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: AppSpacing.lg),
          child: SegmentedButton<BookingsTab>(
            segments: const [
              ButtonSegment(
                value: BookingsTab.upcoming,
                label: Text('Upcoming'),
                icon: Icon(Icons.upcoming_outlined),
              ),
              ButtonSegment(
                value: BookingsTab.past,
                label: Text('Past'),
                icon: Icon(Icons.history),
              ),
            ],
            selected: {state.tab},
            onSelectionChanged: (selection) =>
                controller.setTab(selection.first),
          ),
        ),
        const SizedBox(height: AppSpacing.sm),
        Expanded(
          child: AsyncValueView<PagedResult<Reservation>>(
            value: state.value,
            onRetry: controller.load,
            data: (result) => PaginatedListView<Reservation>(
              items: result.items,
              page: result.page,
              pageSize: result.pageSize,
              totalCount: result.totalCount,
              hasNext: result.hasNext,
              hasPrevious: result.hasPrevious,
              onNextPage: controller.nextPage,
              onPreviousPage: controller.prevPage,
              padding: const EdgeInsets.symmetric(
                horizontal: AppSpacing.lg,
                vertical: AppSpacing.sm,
              ),
              separator: const SizedBox(height: AppSpacing.sm),
              emptyPlaceholder: _EmptyBookings(tab: state.tab),
              itemBuilder: (context, reservation, _) => BookingSummaryTile(
                reservation: reservation,
                onTap: () => context.push(
                  ClientRoutes.bookingDetailPath(reservation.id),
                ),
              ),
            ),
          ),
        ),
      ],
    );
  }
}

/// The per-tab empty state — no upcoming vs. no past bookings.
class _EmptyBookings extends StatelessWidget {
  const _EmptyBookings({required this.tab});

  final BookingsTab tab;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final message = tab == BookingsTab.upcoming
        ? 'No upcoming bookings. Book a court to see it here.'
        : 'No past bookings yet.';
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(AppSpacing.xl),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(Icons.event_busy_outlined, color: AppColors.textMuted),
            const SizedBox(height: AppSpacing.xs),
            Text(
              message,
              textAlign: TextAlign.center,
              style: theme.textTheme.bodyMedium
                  ?.copyWith(color: AppColors.textSecondary),
            ),
          ],
        ),
      ),
    );
  }
}
