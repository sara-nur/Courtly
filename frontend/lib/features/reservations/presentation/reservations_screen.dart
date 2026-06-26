import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../app/router/admin_router.dart';
import '../../../core/enums/reservation_status.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../../../core/utils/formatters.dart';
import '../../../core/widgets/async_value_view.dart';
import '../../../core/widgets/date_time_picker_field.dart';
import '../../../core/widgets/db_dropdown.dart';
import '../../../core/widgets/disabled_action.dart';
import '../../../core/widgets/paginated_list_view.dart';
import '../../../core/widgets/status_badge.dart';
import '../../court_catalog/domain/court_models.dart';
import '../../users/application/user_providers.dart';
import '../application/reservation_providers.dart';
import '../domain/reservation_models.dart';
import 'forms/new_booking_modal.dart';

/// Reservation Management (Feature 15, mockup p.6): a filterable table of every
/// reservation (booking reference, customer, date/time, court, status) that
/// links to the detail master-detail. "+ New Booking" creates a reservation on
/// behalf of a customer. Filters (date range / court / status) are DB-fed and
/// applied at the backend; no raw ids are shown.
class ReservationsScreen extends ConsumerWidget {
  const ReservationsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final state = ref.watch(reservationListControllerProvider);
    final controller = ref.read(reservationListControllerProvider.notifier);

    return Padding(
      padding: AppSpacing.pagePadding,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('Reservation Management',
                        style: theme.textTheme.headlineSmall),
                    const SizedBox(height: AppSpacing.xxs),
                    Text(
                      'View and manage all court bookings — current and past.',
                      style: theme.textTheme.bodyMedium,
                    ),
                  ],
                ),
              ),
              const SizedBox(width: AppSpacing.md),
              const _NewBookingButton(),
            ],
          ),
          const SizedBox(height: AppSpacing.md),
          _FilterBar(filters: state.filters, controller: controller),
          const SizedBox(height: AppSpacing.md),
          const _TableHeader(),
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
                separator: const Divider(height: 1),
                emptyPlaceholder: const _EmptyPlaceholder(),
                itemBuilder: (context, reservation, _) => _ReservationRow(
                  reservation: reservation,
                  onOpen: () => context
                      .go(AdminRoutes.reservationDetail(reservation.id)),
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// "+ New Booking", disabled-with-reason until there is at least one active court
/// and one active customer to book for (a booking can't be created without both).
class _NewBookingButton extends ConsumerWidget {
  const _NewBookingButton();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final courts = ref.watch(reservationCourtLookupProvider);
    final customers = ref.watch(userLookupProvider);

    final reason = () {
      final missing = <String>[
        if (courts.maybeWhen(data: (l) => l.isEmpty, orElse: () => true))
          'an active court',
        if (customers.maybeWhen(data: (l) => l.isEmpty, orElse: () => true))
          'an active customer',
      ];
      return missing.isEmpty ? null : 'Add ${missing.join(' and ')} first.';
    }();

    return DisabledAction(
      enabled: reason == null,
      reason: reason,
      child: ElevatedButton.icon(
        onPressed: () => _openNewBooking(context, ref),
        icon: const Icon(Icons.add, size: 18),
        label: const Text('New Booking'),
      ),
    );
  }

  Future<void> _openNewBooking(BuildContext context, WidgetRef ref) async {
    final created = await showNewBookingModal(context);
    if (created == null) return;
    // Newest-first: reload to page 1 so the new booking shows on top (rubric §6).
    await ref.read(reservationListControllerProvider.notifier).reload();
    if (context.mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Booking ${created.reservation.reference} created.')),
      );
      context.go(AdminRoutes.reservationDetail(created.reservation.id));
    }
  }
}

/// DB-fed filter row: a date range (From/To), a Court dropdown and a Status
/// dropdown, applied at the backend. "Clear" resets every filter.
class _FilterBar extends ConsumerWidget {
  const _FilterBar({required this.filters, required this.controller});

  final ReservationFilters filters;
  final ReservationListController controller;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final courts = ref.watch(reservationCourtLookupProvider);

    return Wrap(
      spacing: AppSpacing.md,
      runSpacing: AppSpacing.sm,
      crossAxisAlignment: WrapCrossAlignment.center,
      children: [
        SizedBox(
          width: 200,
          child: DateTimePickerField(
            label: 'From date',
            value: filters.fromUtc?.toLocal(),
            includeTime: false,
            onChanged: (value) => controller.setFilters(
                filters.copyWith(fromUtc: _dayStartUtc(value))),
          ),
        ),
        SizedBox(
          width: 200,
          child: DateTimePickerField(
            label: 'To date',
            // Display the inclusive day (we store the exclusive next-midnight).
            value: filters.toUtc?.toLocal().subtract(const Duration(days: 1)),
            includeTime: false,
            onChanged: (value) =>
                controller.setFilters(filters.copyWith(toUtc: _dayEndUtc(value))),
          ),
        ),
        SizedBox(
          width: 200,
          child: DbDropdown<Court?>(
            label: 'Court',
            value: courts.maybeWhen(
              data: (list) => _byId(list, filters.courtId),
              orElse: () => null,
            ),
            items: [
              null,
              ...courts.maybeWhen(data: (list) => list, orElse: () => const []),
            ],
            itemLabel: (c) => c?.name ?? 'All courts',
            onChanged: (c) => controller.setFilters(
              c == null
                  ? filters.copyWith(clearCourt: true)
                  : filters.copyWith(courtId: c.id),
            ),
          ),
        ),
        SizedBox(
          width: 200,
          child: DbDropdown<ReservationStatus?>(
            label: 'Status',
            value: filters.status,
            items: const [null, ...ReservationStatus.values],
            itemLabel: (s) => s?.label ?? 'All statuses',
            onChanged: (s) => controller.setFilters(
              s == null
                  ? filters.copyWith(clearStatus: true)
                  : filters.copyWith(status: s),
            ),
          ),
        ),
        TextButton.icon(
          onPressed: controller.clearFilters,
          icon: const Icon(Icons.clear_all, size: 18),
          label: const Text('Clear'),
        ),
      ],
    );
  }

  /// Local calendar day → UTC start (inclusive lower bound for the slot start).
  static DateTime _dayStartUtc(DateTime value) =>
      DateTime.utc(value.year, value.month, value.day);

  /// Local calendar day → UTC next-midnight (exclusive upper bound).
  static DateTime _dayEndUtc(DateTime value) =>
      DateTime.utc(value.year, value.month, value.day).add(const Duration(days: 1));

  Court? _byId(List<Court> courts, int? id) {
    if (id == null) return null;
    for (final c in courts) {
      if (c.id == id) return c;
    }
    return null;
  }
}

/// The table's column header (aligned with [_ReservationRow]'s flex weights).
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
          Expanded(flex: 3, child: Text('BOOKING', style: style)),
          Expanded(flex: 5, child: Text('CUSTOMER', style: style)),
          Expanded(flex: 4, child: Text('DATE & TIME', style: style)),
          Expanded(flex: 3, child: Text('COURT', style: style)),
          Expanded(flex: 3, child: Text('STATUS', style: style)),
          const SizedBox(width: 48, child: Text('')),
        ],
      ),
    );
  }
}

class _ReservationRow extends StatelessWidget {
  const _ReservationRow({required this.reservation, required this.onOpen});

  final Reservation reservation;
  final VoidCallback onOpen;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final r = reservation;
    final start = r.slotStartUtc.toLocal();
    final end = r.slotEndUtc.toLocal();

    return InkWell(
      onTap: onOpen,
      child: Padding(
        padding: const EdgeInsets.symmetric(
            horizontal: AppSpacing.sm, vertical: AppSpacing.sm),
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
              child: Row(
                children: [
                  CircleAvatar(
                    radius: AppSpacing.avatarSm / 2,
                    backgroundColor: AppColors.primarySoft,
                    child: Text(
                      _initials(r.userName),
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
                        Text(r.userName,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: theme.textTheme.bodyMedium
                                ?.copyWith(fontWeight: FontWeight.w600)),
                        if (r.userEmail != null && r.userEmail!.isNotEmpty)
                          Text(r.userEmail!,
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
              flex: 4,
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
              child: Align(
                alignment: Alignment.centerLeft,
                child: _CourtChip(name: r.courtName),
              ),
            ),
            Expanded(
              flex: 3,
              child: Align(
                alignment: Alignment.centerLeft,
                child: StatusBadge(label: r.status.label, tone: r.status.tone),
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

class _CourtChip extends StatelessWidget {
  const _CourtChip({required this.name});

  final String name;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(
          horizontal: AppSpacing.sm, vertical: AppSpacing.xxs),
      decoration: BoxDecoration(
        color: AppColors.surfaceMuted,
        borderRadius: AppSpacing.brSm,
      ),
      child: Text(name,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: Theme.of(context).textTheme.bodySmall),
    );
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
          const Icon(Icons.event_busy_outlined, color: AppColors.textMuted),
          const SizedBox(height: AppSpacing.xs),
          Text(
            'No reservations match these filters',
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
