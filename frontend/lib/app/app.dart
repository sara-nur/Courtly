import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../core/app_flavor.dart';
import '../core/theme/app_theme.dart';
import 'router/admin_router.dart';
import 'router/client_router.dart';

/// Root widget shared by both entrypoints. The [flavor] selects which router
/// (admin top-nav shell vs client bottom-nav shell) and window title to use,
/// while both share the single [AppTheme].
///
/// The admin router comes from `adminRouterProvider` because it carries the
/// auth guard (Feature 8); the client router stays a plain fixture router until
/// its auth lands in Feature 22. Both are created once and kept stable.
class CourtlyApp extends ConsumerStatefulWidget {
  const CourtlyApp({super.key, required this.flavor});

  final AppFlavor flavor;

  @override
  ConsumerState<CourtlyApp> createState() => _CourtlyAppState();
}

class _CourtlyAppState extends ConsumerState<CourtlyApp> {
  GoRouter? _clientRouter;

  String get _title =>
      widget.flavor == AppFlavor.admin ? 'Courtly Admin' : 'Courtly';

  @override
  Widget build(BuildContext context) {
    // `adminRouterProvider` is a cached Provider, so watching it returns the
    // same GoRouter across rebuilds (state preserved); the client router is
    // built once and memoized locally.
    final router = widget.flavor == AppFlavor.admin
        ? ref.watch(adminRouterProvider)
        : (_clientRouter ??= buildClientRouter());

    return MaterialApp.router(
      title: _title,
      debugShowCheckedModeBanner: false,
      theme: AppTheme.light,
      routerConfig: router,
    );
  }
}
