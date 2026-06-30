import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';
import '../../../core/widgets/confirm_dialog.dart';
import '../../auth/application/auth_controller.dart';

/// Client Profile tab. For feature 22 it shows the signed-in user and the
/// sign-out action (the only place the client app ends its session). Full
/// profile edit + change-password land in feature 28.
class ClientProfileScreen extends ConsumerWidget {
  const ClientProfileScreen({super.key});

  Future<void> _signOut(BuildContext context, WidgetRef ref) async {
    final confirmed = await ConfirmDialog.show(
      context,
      title: 'Sign out',
      message: 'Sign out of Courtly?',
      confirmLabel: 'Sign out',
      icon: Icons.logout,
    );
    if (confirmed) {
      await ref.read(authControllerProvider.notifier).logout();
      // The router redirect returns to the sign-in screen.
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final user = ref.watch(authControllerProvider).user;

    return SingleChildScrollView(
      padding: const EdgeInsets.all(AppSpacing.lg),
      child: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 480),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const SizedBox(height: AppSpacing.lg),
              const CircleAvatar(
                radius: 36,
                child: Icon(Icons.person_outline, size: 36),
              ),
              const SizedBox(height: AppSpacing.md),
              Text(
                user?.displayName ?? '',
                textAlign: TextAlign.center,
                style: theme.textTheme.titleLarge,
              ),
              const SizedBox(height: AppSpacing.xxs),
              Text(
                user?.email ?? '',
                textAlign: TextAlign.center,
                style: theme.textTheme.bodyMedium
                    ?.copyWith(color: AppColors.textSecondary),
              ),
              const SizedBox(height: AppSpacing.xl),
              Card(
                child: ListTile(
                  leading: const Icon(Icons.logout, color: AppColors.danger),
                  title: const Text('Sign out'),
                  onTap: () => _signOut(context, ref),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
