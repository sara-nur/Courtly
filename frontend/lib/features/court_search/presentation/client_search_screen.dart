import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../../../core/widgets/app_text_field.dart';
import '../../../core/widgets/async_value_view.dart';
import '../../../core/widgets/paginated_list_view.dart';
import '../../court_catalog/domain/court_models.dart';
import '../../court_catalog/presentation/widgets/client_court_card.dart';
import '../application/search_controller.dart';
import 'widgets/search_filter_modal.dart';

/// Client (mobile) Search tab. A debounced search field drives
/// [CourtSearchController.setQuery]; a "Filters" button opens [SearchFilterModal] and
/// applies the returned [CourtSearchFilters]. Results render as a paginated list
/// of full-width [ClientCourtCard]s wired to the controller's pager.
///
/// This screen lives inside the client shell (which supplies the Scaffold +
/// AppBar + bottom nav), so it builds a bounded-height column body only — no
/// Scaffold of its own.
class ClientSearchScreen extends ConsumerStatefulWidget {
  const ClientSearchScreen({super.key});

  @override
  ConsumerState<ClientSearchScreen> createState() => _ClientSearchScreenState();
}

class _ClientSearchScreenState extends ConsumerState<ClientSearchScreen> {
  final TextEditingController _searchController = TextEditingController();

  /// Debounce so a search fires ~400ms after the user stops typing, not on
  /// every keystroke.
  static const Duration _debounce = Duration(milliseconds: 400);
  Timer? _debounceTimer;

  @override
  void initState() {
    super.initState();
    // Seed the field from any query already in the controller state (e.g. when
    // the tab is rebuilt) so the bar stays in sync.
    _searchController.text = ref.read(searchControllerProvider).filters.query ?? '';
  }

  @override
  void dispose() {
    _debounceTimer?.cancel();
    _searchController.dispose();
    super.dispose();
  }

  void _onQueryChanged(String value) {
    _debounceTimer?.cancel();
    _debounceTimer = Timer(_debounce, () {
      ref.read(searchControllerProvider.notifier).setQuery(value);
    });
  }

  Future<void> _openFilters() async {
    final controller = ref.read(searchControllerProvider.notifier);
    final current = ref.read(searchControllerProvider).filters;

    await showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.transparent,
      builder: (sheetContext) => SearchFilterModal(
        initial: current,
        onApply: (filters) {
          controller.applyFilters(filters);
          Navigator.of(sheetContext).pop();
        },
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(searchControllerProvider);
    final controller = ref.read(searchControllerProvider.notifier);

    return Column(
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(
            AppSpacing.md,
            AppSpacing.md,
            AppSpacing.md,
            AppSpacing.xs,
          ),
          child: Row(
            children: [
              Expanded(
                child: AppTextField(
                  controller: _searchController,
                  hint: 'Search courts',
                  prefixIcon: Icons.search,
                  textInputAction: TextInputAction.search,
                  onChanged: _onQueryChanged,
                ),
              ),
              const SizedBox(width: AppSpacing.xs),
              _FiltersButton(
                active: !state.filters.isEmpty,
                onPressed: _openFilters,
              ),
            ],
          ),
        ),
        Expanded(
          child: AsyncValueView<PagedResult<Court>>(
            value: state.results,
            onRetry: controller.retry,
            data: (paged) => PaginatedListView<Court>(
              items: paged.items,
              page: paged.page,
              pageSize: paged.pageSize,
              totalCount: paged.totalCount,
              hasNext: paged.hasNext,
              hasPrevious: paged.hasPrevious,
              onNextPage: controller.nextPage,
              onPreviousPage: controller.previousPage,
              padding: const EdgeInsets.all(AppSpacing.md),
              separator: const SizedBox(height: AppSpacing.md),
              itemBuilder: (context, court, _) => ClientCourtCard(court: court),
              emptyPlaceholder: const _EmptyResults(),
            ),
          ),
        ),
      ],
    );
  }
}

/// The "Filters" affordance: a bordered icon button that fills with the primary
/// tint when any filter is active so the user can see filters are applied.
class _FiltersButton extends StatelessWidget {
  const _FiltersButton({required this.active, required this.onPressed});

  final bool active;
  final VoidCallback onPressed;

  @override
  Widget build(BuildContext context) {
    return Tooltip(
      message: 'Filters',
      child: IconButton(
        onPressed: onPressed,
        icon: const Icon(Icons.tune),
        color: active ? AppColors.primary : AppColors.textSecondary,
        style: IconButton.styleFrom(
          backgroundColor: active ? AppColors.primarySoft : AppColors.surfaceMuted,
          shape: const RoundedRectangleBorder(borderRadius: AppSpacing.brMd),
        ),
      ),
    );
  }
}

/// Empty state shown when no court matches the current query + filters.
class _EmptyResults extends StatelessWidget {
  const _EmptyResults();

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
              Icons.search_off,
              size: AppSpacing.xxl,
              color: AppColors.textMuted,
            ),
            const SizedBox(height: AppSpacing.md),
            Text(
              'No courts match your filters',
              textAlign: TextAlign.center,
              style: theme.textTheme.titleMedium
                  ?.copyWith(color: AppColors.textSecondary),
            ),
          ],
        ),
      ),
    );
  }
}
