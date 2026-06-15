import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'app/app.dart';
import 'core/app_flavor.dart';
import 'core/env/app_config.dart';

/// Mobile client entrypoint (Android). Stripe init lands in feature 26.
///
/// API base URL comes from `--dart-define=API_BASE_URL=...`, defaulting to
/// `http://10.0.2.2:5000` (the Android emulator's alias for the host).
void main() {
  const flavor = AppFlavor.client;
  runApp(
    ProviderScope(
      overrides: [
        appConfigProvider.overrideWithValue(AppConfig.resolve(flavor)),
      ],
      child: const CourtlyApp(flavor: flavor),
    ),
  );
}
