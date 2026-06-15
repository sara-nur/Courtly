import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../theme/app_colors.dart';
import '../theme/app_spacing.dart';

/// Uniform loading, error, and data rendering for a Riverpod [AsyncValue].
///
/// Provides sensible default loading and error states while allowing callers
/// to override any of them. When [onRetry] is supplied, the default error
/// state shows a retry action.
class AsyncValueView<T> extends StatelessWidget {
  const AsyncValueView({
    super.key,
    required this.value,
    required this.data,
    this.loading,
    this.error,
    this.onRetry,
  });

  /// The async state to render.
  final AsyncValue<T> value;

  /// Builder for the resolved data.
  final Widget Function(T data) data;

  /// Optional override for the loading state.
  final Widget Function()? loading;

  /// Optional override for the error state.
  final Widget Function(Object error, StackTrace? stack)? error;

  /// Optional retry callback shown in the default error state.
  final VoidCallback? onRetry;

  @override
  Widget build(BuildContext context) {
    return value.when(
      data: data,
      loading: () =>
          loading?.call() ??
          const Center(child: CircularProgressIndicator()),
      error: (e, st) => error?.call(e, st) ?? _DefaultError(onRetry: onRetry),
    );
  }
}

class _DefaultError extends StatelessWidget {
  const _DefaultError({this.onRetry});

  final VoidCallback? onRetry;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          const Icon(Icons.error_outline, color: AppColors.danger),
          const SizedBox(height: AppSpacing.xs),
          Text(
            'Something went wrong',
            style: Theme.of(context)
                .textTheme
                .bodyMedium
                ?.copyWith(color: AppColors.textSecondary),
          ),
          if (onRetry != null) ...[
            const SizedBox(height: AppSpacing.xs),
            TextButton(
              onPressed: onRetry,
              child: const Text('Retry'),
            ),
          ],
        ],
      ),
    );
  }
}
