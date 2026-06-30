import 'package:flutter/material.dart';

import '../../../../core/network/api_exception.dart';
import '../../../../core/theme/app_colors.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../core/utils/formatters.dart';
import '../../../../core/utils/image_urls.dart';
import '../../../../core/widgets/async_value_view.dart';
import '../../../../core/widgets/confirm_dialog.dart';
import '../../../../core/widgets/entity_list_tile.dart';
import '../../../../core/widgets/paginated_list_view.dart';
import '../../../../core/widgets/status_badge.dart';
import '../../application/news_providers.dart';
import '../../domain/news_models.dart';

/// The admin news list pane: a search box, a publish-state filter, a "+ New
/// article" button, and a paginated list of [EntityListTile] rows (thumbnail +
/// title + "published date · author", with a Published/Hidden [StatusBadge] and
/// edit/delete actions). Purely callback-driven — [NewsScreen] wires the
/// controller — so it mirrors the Feature 9 `ReferenceListView`.
///
/// Rubric specifics handled here:
///  - **No IDs** are ever rendered — only the title, date, author and image.
///  - **Delete** opens a destructive [ConfirmDialog]; if [onDelete] throws an
///    [ApiException] its message is surfaced as the reason via a [SnackBar].
class NewsListView extends StatelessWidget {
  const NewsListView({
    super.key,
    required this.state,
    required this.baseUrl,
    required this.onAdd,
    required this.onEdit,
    required this.onDelete,
    required this.onSearch,
    required this.onFilterChanged,
    required this.onNextPage,
    required this.onPrevPage,
    required this.onRetry,
  });

  /// Current page state (loading/error/data) plus the search + filter inputs.
  final NewsListState state;

  /// API base URL used to turn the relative image path into an absolute URL.
  final String baseUrl;

  final VoidCallback onAdd;
  final void Function(News item) onEdit;

  /// Deletes [item]. May throw [ApiException]; its message becomes the reason.
  final Future<void> Function(News item) onDelete;

  final ValueChanged<String> onSearch;
  final ValueChanged<NewsActiveFilter> onFilterChanged;
  final VoidCallback onNextPage;
  final VoidCallback onPrevPage;
  final VoidCallback onRetry;

  Future<void> _confirmDelete(BuildContext context, News item) async {
    final confirmed = await ConfirmDialog.show(
      context,
      title: 'Delete article',
      message: 'Delete "${item.title}"? This cannot be undone.',
      confirmLabel: 'Delete',
      destructive: true,
      icon: Icons.delete_outline,
    );
    if (!confirmed) return;

    try {
      await onDelete(item);
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('"${item.title}" deleted.')),
        );
      }
    } on ApiException catch (e) {
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

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: AppSpacing.pagePadding,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          _Toolbar(
            activeFilter: state.activeFilter,
            onSearch: onSearch,
            onFilterChanged: onFilterChanged,
            onAdd: onAdd,
          ),
          const SizedBox(height: AppSpacing.md),
          Expanded(
            child: AsyncValueView<PagedResult<News>>(
              value: state.value,
              onRetry: onRetry,
              data: (result) => PaginatedListView<News>(
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
                  title: item.title,
                  subtitle:
                      '${Formatters.dateTime(item.publishedAtUtc.toLocal())} · ${item.authorName}',
                  imageUrl: item.imageUrl == null
                      ? null
                      : absoluteImageUrl(baseUrl, item.imageUrl!),
                  leadingFallbackIcon: Icons.article_outlined,
                  trailing: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      StatusBadge(
                        label: item.isActive ? 'Published' : 'Hidden',
                        tone: item.isActive
                            ? StatusTone.success
                            : StatusTone.neutral,
                      ),
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
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// Search field + publish-state filter + "+ New article" button row.
class _Toolbar extends StatelessWidget {
  const _Toolbar({
    required this.activeFilter,
    required this.onSearch,
    required this.onFilterChanged,
    required this.onAdd,
  });

  final NewsActiveFilter activeFilter;
  final ValueChanged<String> onSearch;
  final ValueChanged<NewsActiveFilter> onFilterChanged;
  final VoidCallback onAdd;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        Expanded(
          child: TextField(
            onChanged: onSearch,
            decoration: InputDecoration(
              hintText: 'Search news…',
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
        DropdownButton<NewsActiveFilter>(
          value: activeFilter,
          underline: const SizedBox.shrink(),
          onChanged: (f) {
            if (f != null) onFilterChanged(f);
          },
          items: const [
            DropdownMenuItem(value: NewsActiveFilter.all, child: Text('All')),
            DropdownMenuItem(
                value: NewsActiveFilter.active, child: Text('Published')),
            DropdownMenuItem(
                value: NewsActiveFilter.hidden, child: Text('Hidden')),
          ],
        ),
        const SizedBox(width: AppSpacing.md),
        ElevatedButton.icon(
          onPressed: onAdd,
          icon: const Icon(Icons.add, size: 18),
          label: const Text('New article'),
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
          const Icon(Icons.article_outlined, color: AppColors.textMuted),
          const SizedBox(height: AppSpacing.xs),
          Text(
            'No news yet',
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
