import 'package:flutter/material.dart';

import '../../../app/shell/courtly_logo.dart';
import '../../../core/theme/app_spacing.dart';

/// Shown for the brief window while a stored session is validated on startup
/// (auth status `unknown`), so the admin shell never flashes before the redirect
/// resolves.
class AuthSplashScreen extends StatelessWidget {
  const AuthSplashScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return const Scaffold(
      body: Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            CourtlyLogo(showAdminSuffix: true),
            SizedBox(height: AppSpacing.lg),
            SizedBox(
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
