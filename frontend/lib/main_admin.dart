import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'app/app.dart';
import 'core/app_flavor.dart';
import 'core/env/app_config.dart';

/// Desktop admin entrypoint (macOS dev / Windows submission).
///
/// API base URL comes from `--dart-define=API_BASE_URL=...`, defaulting to
/// `http://localhost:5000` for the desktop build.
void main() {
  const flavor = AppFlavor.admin;
  runApp(
    ProviderScope(
      overrides: [
        appConfigProvider.overrideWithValue(AppConfig.resolve(flavor)),
      ],
      child: const CourtlyApp(flavor: flavor),
    ),
  );
}
