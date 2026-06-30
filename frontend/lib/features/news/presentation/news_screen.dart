import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/env/app_config.dart';
import '../../../core/theme/app_spacing.dart';
import '../application/news_providers.dart';
import 'forms/news_form.dart';
import 'widgets/news_list_view.dart';

/// News / announcements admin (Feature 21): the "News" nav section. A header
/// over a [NewsListView] wired to [newsListControllerProvider]; add/edit open the
/// [showNewsForm] dialog. Deleting refreshes the current page; creating reloads
/// from page 1 so the newest article appears on top without a manual refresh.
class NewsScreen extends ConsumerWidget {
  const NewsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final controller = ref.read(newsListControllerProvider.notifier);
    final state = ref.watch(newsListControllerProvider);
    final baseUrl = ref.watch(appConfigProvider).apiBaseUrl;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(
            AppSpacing.lg,
            AppSpacing.lg,
            AppSpacing.lg,
            0,
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text('News & Announcements', style: theme.textTheme.headlineSmall),
              const SizedBox(height: AppSpacing.xxs),
              Text(
                'Publish announcements for the community app.',
                style: theme.textTheme.bodyMedium,
              ),
            ],
          ),
        ),
        const SizedBox(height: AppSpacing.sm),
        Expanded(
          child: NewsListView(
            state: state,
            baseUrl: baseUrl,
            onAdd: () => showNewsForm(context, ref),
            onEdit: (item) => showNewsForm(context, ref, existing: item),
            onDelete: (item) => ref
                .read(newsRepositoryProvider)
                .delete(item.id)
                .then((_) => controller.refresh()),
            onSearch: controller.setSearch,
            onFilterChanged: controller.setActiveFilter,
            onNextPage: controller.nextPage,
            onPrevPage: controller.prevPage,
            onRetry: controller.load,
          ),
        ),
      ],
    );
  }
}
