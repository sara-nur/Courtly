import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../core/app_flavor.dart';
import '../core/theme/app_theme.dart';
import 'router/admin_router.dart';
import 'router/client_router.dart';

/// Root widget shared by both entrypoints. The [flavor] selects which router
/// (admin top-nav shell vs client bottom-nav shell) and window title to use,
/// while both share the single [AppTheme].
///
/// Both routers come from cached Providers carrying their auth guard
/// (`adminRouterProvider`, Feature 8; `clientRouterProvider`, Feature 22), so
/// watching returns the same GoRouter across rebuilds with state preserved.
class CourtlyApp extends ConsumerWidget {
  const CourtlyApp({super.key, required this.flavor});

  final AppFlavor flavor;

  String get _title => flavor == AppFlavor.admin ? 'Courtly Admin' : 'Courtly';

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final router = flavor == AppFlavor.admin
        ? ref.watch(adminRouterProvider)
        : ref.watch(clientRouterProvider);

    return MaterialApp.router(
      title: _title,
      debugShowCheckedModeBanner: false,
      theme: AppTheme.light,
      routerConfig: router,
    );
  }
}
