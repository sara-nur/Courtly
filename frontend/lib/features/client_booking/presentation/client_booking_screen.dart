import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../app/router/client_router.dart';
import '../../../core/network/api_exception.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../../../core/utils/formatters.dart';
import '../../../core/widgets/async_value_view.dart';
import '../../../core/widgets/confirm_dialog.dart';
import '../../client_home/application/home_controller.dart';
import '../../court_catalog/application/court_providers.dart';
import '../../court_catalog/domain/court_models.dart';
import '../application/booking_controller.dart';
import 'widgets/booking_date_strip.dart';
import 'widgets/slot_grid.dart';

/// The client booking screen (F25, mockup p.9 §3.2.3). Reached from a court's
/// **Book Now**: pick a day on the rolling date strip → the day's slots load
/// (bucketed Morning/Afternoon/Evening; taken/past disabled) → pick a free slot →
/// the sticky bar shows the server price → **Confirm Booking** creates a Pending
/// reservation and routes to the confirmation. The screen only *selects*; the
/// overlap/precondition checks and the price are the server's (F13/F14).
class ClientBookingScreen extends ConsumerStatefulWidget {
  const ClientBookingScreen({super.key, required this.courtId});

  final int courtId;

  /// How many days ahead the date strip offers (matches F13's rolling horizon).
  static const int bookingWindowDays = 30;

  @override
  ConsumerState<ClientBookingScreen> createState() => _ClientBookingScreenState();
}

class _ClientBookingScreenState extends ConsumerState<ClientBookingScreen> {
  late final DateTime _firstDate;
  late DateTime _selectedDate;
  AvailabilitySlot? _selectedSlot;

  @override
  void initState() {
    super.initState();
    final now = DateTime.now();
    _firstDate = DateTime(now.year, now.month, now.day);
    _selectedDate = _firstDate;
  }

  ({int courtId, DateTime date}) get _availabilityKey =>
      (courtId: widget.courtId, date: _selectedDate);

  void _onDateSelected(DateTime date) {
    setState(() {
      _selectedDate = date;
      _selectedSlot = null; // a slot on the old day no longer applies
    });
  }

  void _onSlotSelected(AvailabilitySlot slot) {
    setState(() => _selectedSlot = slot);
  }

  Future<void> _confirm() async {
    final slot = _selectedSlot;
    if (slot == null) return;

    final courtName =
        ref.read(courtByIdProvider(widget.courtId)).valueOrNull?.name ?? 'this court';
    final confirmed = await ConfirmDialog.show(
      context,
      title: 'Confirm your booking',
      message: '$courtName on ${Formatters.date(_selectedDate)} at '
          '${Formatters.time(slot.startUtc.toLocal())} for ${Formatters.money(slot.price)}.\n\n'
          'This creates a pending reservation — complete payment to confirm it.',
      confirmLabel: 'Confirm booking',
      icon: Icons.event_available_outlined,
    );
    if (!confirmed || !mounted) return;

    final created = await ref.read(bookingControllerProvider.notifier).submit(slot.id);
    if (!mounted) return;

    if (created != null) {
      // Refresh Home's feed (its "Recent Bookings" section loads via
      // homeDataProvider) so the new Pending booking appears without a manual
      // reload. Home is kept alive in the shell's IndexedStack, so invalidating
      // now re-fetches it before we navigate back.
      ref.invalidate(homeDataProvider);
      context.push(ClientRoutes.bookingCreated, extra: created);
    } else {
      final error = ref.read(bookingControllerProvider).error;
      final message = error is ApiException
          ? error.message
          : 'Could not create the booking. Please try again.';
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(message)));
      // A 409 means the slot was just taken — re-fetch so it shows as Taken.
      ref.invalidate(slotAvailabilityProvider(_availabilityKey));
      setState(() => _selectedSlot = null);
    }
  }

  @override
  Widget build(BuildContext context) {
    final courtValue = ref.watch(courtByIdProvider(widget.courtId));
    final availabilityValue = ref.watch(slotAvailabilityProvider(_availabilityKey));
    final isSubmitting = ref.watch(bookingControllerProvider).isLoading;

    return Scaffold(
      appBar: AppBar(title: const Text('Book a Court')),
      body: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(
                AppSpacing.lg, AppSpacing.md, AppSpacing.lg, 0),
            child: _CourtHeader(courtValue: courtValue),
          ),
          Padding(
            padding: const EdgeInsets.fromLTRB(
                AppSpacing.lg, AppSpacing.md, AppSpacing.lg, 0),
            child: BookingDateStrip(
              selectedDate: _selectedDate,
              firstDate: _firstDate,
              dayCount: ClientBookingScreen.bookingWindowDays,
              onDateSelected: _onDateSelected,
            ),
          ),
          Expanded(
            child: SingleChildScrollView(
              padding: const EdgeInsets.fromLTRB(
                  AppSpacing.lg, AppSpacing.md, AppSpacing.lg, AppSpacing.lg),
              child: AsyncValueView<DayAvailability>(
                value: availabilityValue,
                onRetry: () => ref.invalidate(slotAvailabilityProvider(_availabilityKey)),
                data: (day) => SlotGrid(
                  day: day,
                  selectedSlotId: _selectedSlot?.id,
                  onSelected: _onSlotSelected,
                  now: DateTime.now(),
                ),
              ),
            ),
          ),
        ],
      ),
      bottomNavigationBar: _BookingBottomBar(
        selectedDate: _selectedDate,
        selectedSlot: _selectedSlot,
        isSubmitting: isSubmitting,
        onConfirm: _confirm,
      ),
    );
  }
}

/// The read-only court context above the calendar (court name + surface / indoor
/// chips). No court-switching here — the court is fixed by the "Book Now" the
/// user came from (see the F25 plan decision).
class _CourtHeader extends StatelessWidget {
  const _CourtHeader({required this.courtValue});

  final AsyncValue<Court> courtValue;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return courtValue.when(
      loading: () => Text(
        'Loading court…',
        style: theme.textTheme.titleMedium?.copyWith(color: AppColors.textMuted),
      ),
      error: (_, __) => Text(
        'Selected court',
        style: theme.textTheme.titleLarge?.copyWith(fontWeight: FontWeight.w800),
      ),
      data: (court) => Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            court.name,
            style: theme.textTheme.titleLarge?.copyWith(fontWeight: FontWeight.w800),
          ),
          const SizedBox(height: AppSpacing.xs),
          Wrap(
            spacing: AppSpacing.xs,
            runSpacing: AppSpacing.xs,
            children: [
              _HeaderChip(label: court.surfaceTypeName.toUpperCase()),
              _HeaderChip(label: court.isIndoor ? 'Indoor' : 'Outdoor'),
            ],
          ),
        ],
      ),
    );
  }
}

class _HeaderChip extends StatelessWidget {
  const _HeaderChip({required this.label});

  final String label;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(
          horizontal: AppSpacing.sm, vertical: AppSpacing.xxs),
      decoration: const BoxDecoration(
        color: AppColors.primarySoft,
        borderRadius: AppSpacing.brPill,
      ),
      child: Text(
        label,
        style: Theme.of(context).textTheme.labelSmall?.copyWith(
              color: AppColors.primaryDark,
              fontWeight: FontWeight.w600,
            ),
      ),
    );
  }
}

/// The sticky "Selected slot / Price / Confirm Booking" bar (mockup bottom).
/// Confirm is disabled until a free slot is picked, and shows a spinner while the
/// create request is in flight.
class _BookingBottomBar extends StatelessWidget {
  const _BookingBottomBar({
    required this.selectedDate,
    required this.selectedSlot,
    required this.isSubmitting,
    required this.onConfirm,
  });

  final DateTime selectedDate;
  final AvailabilitySlot? selectedSlot;
  final bool isSubmitting;
  final Future<void> Function() onConfirm;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final slot = selectedSlot;
    final hasSelection = slot != null;
    final canConfirm = hasSelection && !isSubmitting;

    return Material(
      color: AppColors.surface,
      elevation: 8,
      child: SafeArea(
        top: false,
        child: Padding(
          padding: const EdgeInsets.all(AppSpacing.lg),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'Selected slot',
                          style: theme.textTheme.bodySmall
                              ?.copyWith(color: AppColors.textMuted),
                        ),
                        const SizedBox(height: AppSpacing.xxs),
                        Text(
                          hasSelection
                              ? '${Formatters.date(selectedDate)} · '
                                  '${Formatters.time(slot.startUtc.toLocal())}'
                              : 'Select a time slot',
                          style: theme.textTheme.titleSmall
                              ?.copyWith(fontWeight: FontWeight.w700),
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(width: AppSpacing.md),
                  Column(
                    crossAxisAlignment: CrossAxisAlignment.end,
                    children: [
                      Text(
                        'Price',
                        style: theme.textTheme.bodySmall
                            ?.copyWith(color: AppColors.textMuted),
                      ),
                      const SizedBox(height: AppSpacing.xxs),
                      Text(
                        hasSelection ? Formatters.money(slot.price) : '—',
                        style: theme.textTheme.titleLarge?.copyWith(
                          fontWeight: FontWeight.w800,
                          color: AppColors.primary,
                        ),
                      ),
                    ],
                  ),
                ],
              ),
              const SizedBox(height: AppSpacing.md),
              SizedBox(
                width: double.infinity,
                child: FilledButton.icon(
                  onPressed: canConfirm ? () => onConfirm() : null,
                  icon: isSubmitting
                      ? const SizedBox(
                          width: 18,
                          height: 18,
                          child: CircularProgressIndicator(
                              strokeWidth: 2, color: Colors.white),
                        )
                      : const Icon(Icons.arrow_forward),
                  label: Text(isSubmitting ? 'Booking…' : 'Confirm Booking'),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
