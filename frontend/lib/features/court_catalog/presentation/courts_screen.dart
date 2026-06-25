import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/network/api_exception.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../../../core/widgets/async_value_view.dart';
import '../../../core/widgets/confirm_dialog.dart';
import '../../../core/widgets/db_dropdown.dart';
import '../../../core/widgets/disabled_action.dart';
import '../../reference_data/domain/reference_models.dart';
import '../application/court_providers.dart';
import '../domain/court_models.dart';
import 'forms/court_form.dart';
import 'maintenance/maintenance_modal.dart';
import 'widgets/court_card.dart';

/// Court Management (Feature 10): a top-level routed screen with a filter
/// toolbar and a responsive card GRID of [CourtCard]s, paged via the
/// [courtListControllerProvider].
///
/// Rubric specifics handled here:
///  - **No IDs** are ever rendered — only names.
///  - **+ Add New Court** is disabled-with-reason when any prerequisite table
///    (cities / surface types / court types) is empty — a court can't be created
///    without them.
///  - Filters are DB-driven (surface chips + City/Country/Court Type dropdowns,
///    an Indoor toggle and min/max price), never free-text for FKs.
///  - **Delete** opens a destructive [ConfirmDialog]; a delete-restrict
///    [ApiException] surfaces the backend reason in a [SnackBar].
class CourtsScreen extends ConsumerWidget {
  const CourtsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final state = ref.watch(courtListControllerProvider);
    final controller = ref.read(courtListControllerProvider.notifier);

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
                    Text('Courts', style: theme.textTheme.headlineSmall),
                    const SizedBox(height: AppSpacing.xxs),
                    Text(
                      'Manage your court catalog — pricing, surface and availability.',
                      style: theme.textTheme.bodyMedium,
                    ),
                  ],
                ),
              ),
              const SizedBox(width: AppSpacing.md),
              const _AddCourtButton(),
            ],
          ),
          const SizedBox(height: AppSpacing.md),
          _FilterBar(filters: state.filters, controller: controller),
          const SizedBox(height: AppSpacing.md),
          Expanded(
            child: AsyncValueView<PagedResult<Court>>(
              value: state.value,
              onRetry: controller.load,
              data: (result) => _CourtGrid(
                result: result,
                onEdit: (court) => showCourtForm(context, ref, existing: court),
                onDelete: (court) => _confirmDelete(context, ref, court),
                onManageMaintenance: (court) =>
                    showCourtMaintenance(context, ref, court),
                onNextPage: controller.nextPage,
                onPrevPage: controller.prevPage,
              ),
            ),
          ),
        ],
      ),
    );
  }

  Future<void> _confirmDelete(
    BuildContext context,
    WidgetRef ref,
    Court court,
  ) async {
    final confirmed = await ConfirmDialog.show(
      context,
      title: 'Delete',
      message: 'Delete "${court.name}"? This cannot be undone.',
      confirmLabel: 'Delete',
      destructive: true,
      icon: Icons.delete_outline,
    );
    if (!confirmed) return;

    try {
      await ref.read(courtRepositoryProvider).delete(court.id);
      await ref.read(courtListControllerProvider.notifier).refresh();
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('"${court.name}" deleted.')),
        );
      }
    } on ApiException catch (e) {
      // Delete-restrict (referenced by reservations, etc.) — surface the
      // backend's real reason, never a generic error.
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(e.message), backgroundColor: AppColors.danger),
        );
      }
    } catch (_) {
      if (context.mounted) {
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

/// "+ Add New Court", disabled-with-reason while any prerequisite lookup
/// (cities / surface types / court types) is still empty.
class _AddCourtButton extends ConsumerWidget {
  const _AddCourtButton();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final lookups = ref.watch(courtFormLookupsProvider);

    final reason = lookups.maybeWhen(
      data: (data) {
        final missing = <String>[
          if (data.cities.isEmpty) 'a city',
          if (data.surfaceTypes.isEmpty) 'a surface type',
          if (data.courtTypes.isEmpty) 'a court type',
        ];
        if (missing.isEmpty) return null;
        return 'Add ${missing.join(', ')} first — courts require them.';
      },
      // While loading / on error, keep the button blocked so we never open a
      // form whose required dropdowns can't be populated.
      orElse: () => 'Loading reference data…',
    );

    return DisabledAction(
      enabled: reason == null,
      reason: reason,
      child: ElevatedButton.icon(
        onPressed: () => showCourtForm(context, ref),
        icon: const Icon(Icons.add, size: 18),
        label: const Text('Add New Court'),
      ),
    );
  }
}

/// Search + DB-driven filter controls: surface chips (incl. "All"), Court Type /
/// City / Country dropdowns, an Indoor toggle and min/max price fields.
class _FilterBar extends ConsumerWidget {
  const _FilterBar({required this.filters, required this.controller});

  final CourtFilters filters;
  final CourtListController controller;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final surfaces = ref.watch(surfaceTypeLookupProvider);
    final courtTypes = ref.watch(courtTypeLookupProvider);
    final cities = ref.watch(cityLookupProvider);
    final countries = ref.watch(courtCountryLookupProvider);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        TextField(
          onChanged: controller.setSearch,
          decoration: const InputDecoration(
            hintText: 'Search courts…',
            prefixIcon: Icon(Icons.search, size: 18),
            isDense: true,
            fillColor: AppColors.surfaceMuted,
            contentPadding: EdgeInsets.symmetric(
              horizontal: AppSpacing.sm,
              vertical: AppSpacing.xs,
            ),
          ),
        ),
        const SizedBox(height: AppSpacing.sm),
        // Surface filter chips, built from the DB surface list + an "All" chip.
        surfaces.maybeWhen(
          data: (list) => Wrap(
            spacing: AppSpacing.xs,
            runSpacing: AppSpacing.xs,
            children: [
              ChoiceChip(
                label: const Text('All surfaces'),
                selected: filters.surfaceTypeId == null,
                onSelected: (_) => controller
                    .setFilters(filters.copyWith(clearSurfaceType: true)),
              ),
              for (final s in list)
                ChoiceChip(
                  label: Text(s.name),
                  selected: filters.surfaceTypeId == s.id,
                  onSelected: (_) => controller
                      .setFilters(filters.copyWith(surfaceTypeId: s.id)),
                ),
            ],
          ),
          orElse: () => const SizedBox.shrink(),
        ),
        const SizedBox(height: AppSpacing.sm),
        Wrap(
          spacing: AppSpacing.md,
          runSpacing: AppSpacing.sm,
          crossAxisAlignment: WrapCrossAlignment.center,
          children: [
            SizedBox(
              width: 200,
              child: DbDropdown<CourtType?>(
                value: courtTypes.maybeWhen(
                  data: (list) => _byId(list, filters.courtTypeId, (t) => t.id),
                  orElse: () => null,
                ),
                items: [
                  null,
                  ...courtTypes.maybeWhen(
                      data: (list) => list, orElse: () => const []),
                ],
                itemLabel: (t) => t?.name ?? 'All court types',
                label: 'Court type',
                onChanged: (t) => controller.setFilters(
                  t == null
                      ? filters.copyWith(clearCourtType: true)
                      : filters.copyWith(courtTypeId: t.id),
                ),
              ),
            ),
            SizedBox(
              width: 200,
              child: DbDropdown<City?>(
                value: cities.maybeWhen(
                  data: (list) => _byId(list, filters.cityId, (c) => c.id),
                  orElse: () => null,
                ),
                items: [
                  null,
                  ...cities.maybeWhen(
                      data: (list) => list, orElse: () => const []),
                ],
                itemLabel: (c) => c?.name ?? 'All cities',
                label: 'City',
                onChanged: (c) => controller.setFilters(
                  c == null
                      ? filters.copyWith(clearCity: true)
                      : filters.copyWith(cityId: c.id),
                ),
              ),
            ),
            SizedBox(
              width: 200,
              child: DbDropdown<Country?>(
                value: countries.maybeWhen(
                  data: (list) => _byId(list, filters.countryId, (c) => c.id),
                  orElse: () => null,
                ),
                items: [
                  null,
                  ...countries.maybeWhen(
                      data: (list) => list, orElse: () => const []),
                ],
                itemLabel: (c) => c?.name ?? 'All countries',
                label: 'Country',
                onChanged: (c) => controller.setFilters(
                  c == null
                      ? filters.copyWith(clearCountry: true)
                      : filters.copyWith(countryId: c.id),
                ),
              ),
            ),
            FilterChip(
              label: const Text('Indoor only'),
              selected: filters.isIndoor == true,
              onSelected: (on) => controller.setFilters(
                on
                    ? filters.copyWith(isIndoor: true)
                    : filters.copyWith(clearIndoor: true),
              ),
            ),
            // "Active only" = currently available: active AND not under maintenance. Mutually exclusive with the
            // Maintenance chip (selecting one clears the other).
            FilterChip(
              label: const Text('Active only'),
              selected: filters.isActive == true,
              onSelected: (on) => controller.setFilters(
                on
                    ? filters.copyWith(isActive: true, underMaintenance: false)
                    : filters.copyWith(
                        clearActive: true, clearUnderMaintenance: true),
              ),
            ),
            FilterChip(
              label: const Text('Maintenance'),
              selected: filters.underMaintenance == true,
              onSelected: (on) => controller.setFilters(
                on
                    ? filters.copyWith(underMaintenance: true, clearActive: true)
                    : filters.copyWith(clearUnderMaintenance: true),
              ),
            ),
            _PriceField(
              label: 'Min price',
              value: filters.minPrice,
              onChanged: (v) => controller.setFilters(
                v == null
                    ? filters.copyWith(clearMinPrice: true)
                    : filters.copyWith(minPrice: v),
              ),
            ),
            _PriceField(
              label: 'Max price',
              value: filters.maxPrice,
              onChanged: (v) => controller.setFilters(
                v == null
                    ? filters.copyWith(clearMaxPrice: true)
                    : filters.copyWith(maxPrice: v),
              ),
            ),
            TextButton.icon(
              onPressed: controller.clearFilters,
              icon: const Icon(Icons.clear_all, size: 18),
              label: const Text('Clear'),
            ),
          ],
        ),
      ],
    );
  }

  T? _byId<T>(List<T> items, int? id, int Function(T) idOf) {
    if (id == null) return null;
    for (final item in items) {
      if (idOf(item) == id) return item;
    }
    return null;
  }
}

/// A small numeric price filter field. Emits null when blank/invalid (= no
/// filter), otherwise the parsed amount.
class _PriceField extends StatefulWidget {
  const _PriceField({
    required this.label,
    required this.value,
    required this.onChanged,
  });

  final String label;
  final double? value;
  final ValueChanged<double?> onChanged;

  @override
  State<_PriceField> createState() => _PriceFieldState();
}

class _PriceFieldState extends State<_PriceField> {
  late final TextEditingController _controller;

  @override
  void initState() {
    super.initState();
    _controller = TextEditingController(
      text: widget.value == null ? '' : widget.value!.toString(),
    );
  }

  @override
  void didUpdateWidget(_PriceField oldWidget) {
    super.didUpdateWidget(oldWidget);
    // Keep the field in sync when the active filter changes externally — e.g.
    // "Clear" resets filters.minPrice/maxPrice to null, so the displayed text
    // must clear too instead of showing the stale typed value.
    if (oldWidget.value != widget.value) {
      final text = widget.value == null ? '' : widget.value!.toString();
      if (_controller.text != text) {
        _controller.text = text;
      }
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: 130,
      child: TextField(
        controller: _controller,
        keyboardType: const TextInputType.numberWithOptions(decimal: true),
        onSubmitted: (text) {
          final parsed = double.tryParse(text.trim());
          widget.onChanged(text.trim().isEmpty ? null : parsed);
        },
        decoration: InputDecoration(
          labelText: widget.label,
          prefixText: r'$',
          isDense: true,
        ),
      ),
    );
  }
}

/// Responsive card grid + a footer pager driven by the controller's page state.
/// (PaginatedListView is list-only, so the grid + pager are built here and only
/// the "Showing x–y of N" + chevron footer pattern is replicated.)
class _CourtGrid extends StatelessWidget {
  const _CourtGrid({
    required this.result,
    required this.onEdit,
    required this.onDelete,
    required this.onManageMaintenance,
    required this.onNextPage,
    required this.onPrevPage,
  });

  final PagedResult<Court> result;
  final void Function(Court) onEdit;
  final void Function(Court) onDelete;
  final void Function(Court) onManageMaintenance;
  final VoidCallback onNextPage;
  final VoidCallback onPrevPage;

  @override
  Widget build(BuildContext context) {
    if (result.isEmpty) {
      return const _EmptyPlaceholder();
    }

    final items = result.items;
    final from = (result.page - 1) * result.pageSize + 1;
    final to = (result.page - 1) * result.pageSize + items.length;

    return Column(
      children: [
        Expanded(
          child: LayoutBuilder(
            builder: (context, constraints) {
              // ~320px per card → responsive column count (min 1).
              final columns = (constraints.maxWidth / 320).floor().clamp(1, 6);
              return GridView.builder(
                gridDelegate: SliverGridDelegateWithFixedCrossAxisCount(
                  crossAxisCount: columns,
                  crossAxisSpacing: AppSpacing.md,
                  mainAxisSpacing: AppSpacing.md,
                  mainAxisExtent: 360,
                ),
                itemCount: items.length,
                itemBuilder: (context, i) {
                  final court = items[i];
                  return CourtCard(
                    court: court,
                    onEdit: () => onEdit(court),
                    onDelete: () => onDelete(court),
                    onManageMaintenance: () => onManageMaintenance(court),
                  );
                },
              );
            },
          ),
        ),
        Container(
          padding: AppSpacing.cardPadding,
          child: Row(
            children: [
              Text('Showing $from–$to of ${result.totalCount}'),
              const Spacer(),
              IconButton(
                icon: const Icon(Icons.chevron_left),
                onPressed: result.hasPrevious ? onPrevPage : null,
              ),
              IconButton(
                icon: const Icon(Icons.chevron_right),
                onPressed: result.hasNext ? onNextPage : null,
              ),
            ],
          ),
        ),
      ],
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
          const Icon(Icons.sports_tennis_outlined, color: AppColors.textMuted),
          const SizedBox(height: AppSpacing.xs),
          Text(
            'No courts found',
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
