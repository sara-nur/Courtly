import 'package:flutter/material.dart';

import '../../../app/shell/courtly_logo.dart';
import '../../../core/theme/app_spacing.dart';

/// Shown for the brief window while a stored session is validated on startup
/// (auth status `unknown`), so the shell never flashes before the redirect
/// resolves. Shared by both apps — the client passes [showAdminSuffix] `false`.
class AuthSplashScreen extends StatelessWidget {
  const AuthSplashScreen({super.key, this.showAdminSuffix = true});

  final bool showAdminSuffix;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            CourtlyLogo(showAdminSuffix: showAdminSuffix),
            const SizedBox(height: AppSpacing.lg),
            const SizedBox(
              width: 24,
              height: 24,
              child: CircularProgressIndicator(strokeWidth: 2.5),
            ),
          ],
        ),
      ),
    );
  }
}
