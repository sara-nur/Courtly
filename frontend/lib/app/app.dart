import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../core/app_flavor.dart';
import '../core/theme/app_theme.dart';
import 'router/admin_router.dart';
import 'router/client_router.dart';

/// Root widget shared by both entrypoints. The [flavor] selects which router
/// (admin top-nav shell vs client bottom-nav shell) and window title to use,
/// while both share the single [AppTheme].
class CourtlyApp extends StatefulWidget {
  const CourtlyApp({super.key, required this.flavor});

  final AppFlavor flavor;

  @override
  State<CourtlyApp> createState() => _CourtlyAppState();
}

class _CourtlyAppState extends State<CourtlyApp> {
  late final GoRouter _router =
      widget.flavor == AppFlavor.admin ? buildAdminRouter() : buildClientRouter();

  String get _title =>
      widget.flavor == AppFlavor.admin ? 'Courtly Admin' : 'Courtly';

  @override
  Widget build(BuildContext context) {
    return MaterialApp.router(
      title: _title,
      debugShowCheckedModeBanner: false,
      theme: AppTheme.light,
      routerConfig: _router,
    );
  }
}
