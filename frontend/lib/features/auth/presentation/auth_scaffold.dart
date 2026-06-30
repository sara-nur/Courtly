import 'package:flutter/material.dart';

import '../../../app/shell/courtly_logo.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/app_spacing.dart';

/// Shared chrome for the auth screens (login, register, forgot/reset password):
/// a centered, scrollable card with the Courtly logo, a title and subtitle, then
/// the screen's [child] (its `Form`). Keeps the four screens consistent and DRY.
class AuthScaffold extends StatelessWidget {
  const AuthScaffold({
    super.key,
    required this.title,
    required this.subtitle,
    required this.child,
    this.showAdminSuffix = false,
    this.maxWidth = 400,
  });

  final String title;
  final String subtitle;

  /// The screen-specific body, typically a `Form`.
  final Widget child;

  /// The client app shows the plain logo; the admin app passes `true`.
  final bool showAdminSuffix;
  final double maxWidth;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Scaffold(
      body: Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(AppSpacing.lg),
          child: ConstrainedBox(
            constraints: BoxConstraints(maxWidth: maxWidth),
            child: Card(
              child: Padding(
                padding: const EdgeInsets.all(AppSpacing.xl),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Center(child: CourtlyLogo(showAdminSuffix: showAdminSuffix)),
                    const SizedBox(height: AppSpacing.lg),
                    Text(
                      title,
                      textAlign: TextAlign.center,
                      style: theme.textTheme.headlineSmall,
                    ),
                    const SizedBox(height: AppSpacing.xxs),
                    Text(
                      subtitle,
                      textAlign: TextAlign.center,
                      style: theme.textTheme.bodyMedium,
                    ),
                    const SizedBox(height: AppSpacing.lg),
                    child,
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// Inline banner for a non-field auth failure (bad credentials, blocked role, a
/// connection problem, or a neutral confirmation). Shown above the fields, never
/// as a dialog (rubric §4). [tone] defaults to danger; forgot-password uses an
/// informational tone for its anti-enumeration confirmation.
class AuthBanner extends StatelessWidget {
  const AuthBanner({
    super.key,
    required this.message,
    this.tone = StatusTone.danger,
    this.icon = Icons.error_outline,
  });

  final String message;
  final StatusTone tone;
  final IconData icon;

  @override
  Widget build(BuildContext context) {
    final colors = StatusToneColors.of(tone);
    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.sm,
        vertical: AppSpacing.xs,
      ),
      decoration: BoxDecoration(
        color: colors.background,
        borderRadius: AppSpacing.brSm,
      ),
      child: Row(
        children: [
          Icon(icon, size: 18, color: colors.foreground),
          const SizedBox(width: AppSpacing.xs),
          Expanded(
            child: Text(
              message,
              style: TextStyle(color: colors.foreground, fontSize: 13),
            ),
          ),
        ],
      ),
    );
  }
}
