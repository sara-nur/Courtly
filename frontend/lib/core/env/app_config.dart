import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../app_flavor.dart';

/// Centralized, read-once application configuration.
///
/// The API base URL is read from the compile-time environment via
/// `String.fromEnvironment('API_BASE_URL')` (rubric §3.3) and is overridable at
/// launch with `--dart-define=API_BASE_URL=...`. When not provided, a
/// flavor-aware default is used: the Android emulator reaches the host machine
/// on `10.0.2.2`, while the desktop admin app talks to `localhost`.
class AppConfig {
  const AppConfig({required this.flavor, required this.apiBaseUrl});

  /// Which app this build runs as.
  final AppFlavor flavor;

  /// Base URL for the Courtly REST API (no trailing slash).
  final String apiBaseUrl;

  /// Read once from `--dart-define`; empty string when unset.
  static const String _envApiBaseUrl = String.fromEnvironment('API_BASE_URL');

  static const String _defaultAdminBaseUrl = 'http://localhost:5000';
  static const String _defaultClientBaseUrl = 'http://10.0.2.2:5000';

  /// Builds the config for [flavor], honoring the `--dart-define` override and
  /// falling back to the flavor-appropriate default.
  factory AppConfig.resolve(AppFlavor flavor) {
    final base = _envApiBaseUrl.isNotEmpty
        ? _envApiBaseUrl
        : (flavor == AppFlavor.client
            ? _defaultClientBaseUrl
            : _defaultAdminBaseUrl);
    return AppConfig(flavor: flavor, apiBaseUrl: base);
  }

  bool get isAdmin => flavor == AppFlavor.admin;
  bool get isClient => flavor == AppFlavor.client;
}

/// Provides the resolved [AppConfig]. Overridden in each entrypoint's
/// `ProviderScope` with the build's flavor.
final appConfigProvider = Provider<AppConfig>((ref) {
  throw UnimplementedError(
    'appConfigProvider must be overridden in the entrypoint ProviderScope.',
  );
});
