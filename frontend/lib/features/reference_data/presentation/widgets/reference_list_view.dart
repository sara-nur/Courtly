import 'package:flutter/material.dart';

import '../../../../core/network/api_exception.dart';
import '../../../../core/theme/app_colors.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../core/widgets/async_value_view.dart';
import '../../../../core/widgets/confirm_dialog.dart';
import '../../../../core/widgets/disabled_action.dart';
import '../../../../core/widgets/entity_list_tile.dart';
import '../../../../core/widgets/paginated_list_view.dart';
import '../../application/reference_providers.dart';
import '../../domain/reference_models.dart';

/// Reusable list pane for one reference entity: a search box, a "+ Add" button,
/// and a paginated list of [EntityListTile] rows (name + subtitle, with edit and
/// delete trailing actions). One widget serves all five entities — each tab just
/// wires its own state, builders and callbacks.
///
/// Rubric specifics handled here:
///  - **No IDs** are ever rendered — only [titleOf]/[subtitleOf] text.
///  - **Delete** opens a destructive [ConfirmDialog]; if [onDelete] throws an
///    [ApiException] (e.g. a delete-restrict `BusinessException`), its message is
///    surfaced as the blocked reason via a [SnackBar].
///  - When [addDisabledReason] is non-null the "+ Add" button is wrapped in a
///    [DisabledAction] showing that reason (e.g. cities need a country first).
class ReferenceListView<T> extends StatelessWidget {
  const ReferenceListView({
    super.key,
    required this.state,
    required this.titleOf,
    required this.subtitleOf,
    required this.onAdd,
    required this.onEdit,
    required this.onDelete,
    required this.onSearch,
    required this.onNextPage,
    required this.onPrevPage,
    required this.onRetry,
    this.addLabel = 'Add',
    this.searchHint = 'Search…',
    this.addDisabledReason,
  });

  /// Current page state for this entity (loading/error/data).
  final ReferenceListState<T> state;

  /// Primary row label for an item (a name — never an id).
  final String Function(T item) titleOf;

  /// Optional secondary row label for an item (e.g. a City's country name).
  final String? Function(T item) subtitleOf;

  /// Opens the create form.
  final VoidCallback onAdd;

  /// Opens the edit form for [item].
  final void Function(T item) onEdit;

  /// Deletes [item]. May throw [ApiException]; its message becomes the reason.
  final Future<void> Function(T item) onDelete;

  /// Applies a new search term (controller reloads from page 1).
  final ValueChanged<String> onSearch;

  final VoidCallback onNextPage;
  final VoidCallback onPrevPage;
  final VoidCallback onRetry;

  /// Label/hint copy (e.g. "Add country" / "Search countries…").
  final String addLabel;
  final String searchHint;

  /// When non-null, the "+ Add" button is disabled and explains why.
  final String? addDisabledReason;

  Future<void> _confirmDelete(BuildContext context, T item) async {
    final name = titleOf(item);
    final confirmed = await ConfirmDialog.show(
      context,
      title: 'Delete',
      message: 'Delete "$name"? This cannot be undone.',
      confirmLabel: 'Delete',
      destructive: true,
      icon: Icons.delete_outline,
    );
    if (!confirmed) return;

    try {
      await onDelete(item);
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('"$name" deleted.')),
        );
      }
    } on ApiException catch (e) {
      // Delete-restrict (referenced row) or another business rule — surface the
      // backend's real message as the blocked reason, never a generic error.
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(e.message),
            backgroundColor: AppColors.danger,
          ),
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

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: AppSpacing.pagePadding,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          _Toolbar(
            searchHint: searchHint,
            addLabel: addLabel,
            onSearch: onSearch,
            onAdd: onAdd,
            addDisabledReason: addDisabledReason,
          ),
          const SizedBox(height: AppSpacing.md),
          Expanded(
            child: AsyncValueView<PagedResult<T>>(
              value: state.value,
              onRetry: onRetry,
              data: (result) {
                return PaginatedListView<T>(
                  items: result.items,
                  page: result.page,
                  pageSize: result.pageSize,
                  totalCount: result.totalCount,
                  hasNext: result.hasNext,
                  hasPrevious: result.hasPrevious,
                  onNextPage: onNextPage,
                  onPreviousPage: onPrevPage,
                  separator: const Divider(height: 1),
                  emptyPlaceholder: const _EmptyPlaceholder(),
                  itemBuilder: (context, item, _) => EntityListTile(
                    title: titleOf(item),
                    subtitle: subtitleOf(item),
                    leadingFallbackIcon: Icons.label_outline,
                    trailing: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        IconButton(
                          tooltip: 'Edit',
                          icon: const Icon(Icons.edit_outlined),
                          color: AppColors.textSecondary,
                          onPressed: () => onEdit(item),
                        ),
                        IconButton(
                          tooltip: 'Delete',
                          icon: const Icon(Icons.delete_outline),
                          color: AppColors.danger,
                          onPressed: () => _confirmDelete(context, item),
                        ),
                      ],
                    ),
                  ),
                );
              },
            ),
          ),
        ],
      ),
    );
  }
}

/// Search field + "+ Add" button row. The Add button is wrapped in a
/// [DisabledAction] whenever [addDisabledReason] is supplied.
class _Toolbar extends StatelessWidget {
  const _Toolbar({
    required this.searchHint,
    required this.addLabel,
    required this.onSearch,
    required this.onAdd,
    required this.addDisabledReason,
  });

  final String searchHint;
  final String addLabel;
  final ValueChanged<String> onSearch;
  final VoidCallback onAdd;
  final String? addDisabledReason;

  @override
  Widget build(BuildContext context) {
    final addButton = ElevatedButton.icon(
      onPressed: onAdd,
      icon: const Icon(Icons.add, size: 18),
      label: Text(addLabel),
    );

    return Row(
      children: [
        Expanded(
          child: TextField(
            onChanged: onSearch,
            decoration: InputDecoration(
              hintText: searchHint,
              prefixIcon: const Icon(Icons.search, size: 18),
              isDense: true,
              fillColor: AppColors.surfaceMuted,
              contentPadding: const EdgeInsets.symmetric(
                horizontal: AppSpacing.sm,
                vertical: AppSpacing.xs,
              ),
            ),
          ),
        ),
        const SizedBox(width: AppSpacing.md),
        DisabledAction(
          enabled: addDisabledReason == null,
          reason: addDisabledReason,
          child: addButton,
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
          const Icon(Icons.inbox_outlined, color: AppColors.textMuted),
          const SizedBox(height: AppSpacing.xs),
          Text(
            'Nothing here yet',
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
