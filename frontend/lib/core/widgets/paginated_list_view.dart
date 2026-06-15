import 'package:flutter/material.dart';

import '../theme/app_colors.dart';
import '../theme/app_spacing.dart';

/// A generic, paginated list view that renders [items] with a footer showing
/// the current range and page navigation controls.
///
/// Expects a bounded-height parent because it uses an [Expanded] child. Invokes
/// [onEndReached] when the user scrolls near the bottom (within 200px).
class PaginatedListView<T> extends StatefulWidget {
  const PaginatedListView({
    super.key,
    required this.items,
    required this.itemBuilder,
    this.page = 1,
    this.pageSize = 20,
    this.totalCount = 0,
    this.hasNext = false,
    this.hasPrevious = false,
    this.onNextPage,
    this.onPreviousPage,
    this.onEndReached,
    this.separator,
    this.emptyPlaceholder,
    this.padding,
  });

  final List<T> items;
  final Widget Function(BuildContext, T, int) itemBuilder;
  final int page;
  final int pageSize;
  final int totalCount;
  final bool hasNext;
  final bool hasPrevious;
  final VoidCallback? onNextPage;
  final VoidCallback? onPreviousPage;
  final VoidCallback? onEndReached;
  final Widget? separator;
  final Widget? emptyPlaceholder;
  final EdgeInsetsGeometry? padding;

  @override
  State<PaginatedListView<T>> createState() => _PaginatedListViewState<T>();
}

class _PaginatedListViewState<T> extends State<PaginatedListView<T>> {
  final ScrollController _controller = ScrollController();

  @override
  void initState() {
    super.initState();
    _controller.addListener(_onScroll);
  }

  void _onScroll() {
    final onEndReached = widget.onEndReached;
    if (onEndReached == null) return;
    final position = _controller.position;
    if (position.pixels >= position.maxScrollExtent - 200) {
      onEndReached();
    }
  }

  @override
  void dispose() {
    _controller.removeListener(_onScroll);
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final items = widget.items;
    final from = items.isEmpty ? 0 : (widget.page - 1) * widget.pageSize + 1;
    final to = (widget.page - 1) * widget.pageSize + items.length;

    return Column(
      children: [
        Expanded(
          child: items.isEmpty
              ? (widget.emptyPlaceholder ??
                  Center(
                    child: Text(
                      'No records found',
                      style: TextStyle(color: AppColors.textMuted),
                    ),
                  ))
              : ListView.separated(
                  controller: _controller,
                  padding: widget.padding,
                  itemCount: items.length,
                  separatorBuilder: (_, __) =>
                      widget.separator ?? const Divider(),
                  itemBuilder: (c, i) => widget.itemBuilder(c, items[i], i),
                ),
        ),
        Container(
          padding: AppSpacing.cardPadding,
          child: Row(
            children: [
              Text('Showing $from–$to of ${widget.totalCount}'),
              const Spacer(),
              IconButton(
                icon: const Icon(Icons.chevron_left),
                onPressed: widget.hasPrevious ? widget.onPreviousPage : null,
              ),
              IconButton(
                icon: const Icon(Icons.chevron_right),
                onPressed: widget.hasNext ? widget.onNextPage : null,
              ),
            ],
          ),
        ),
      ],
    );
  }
}
