import 'package:flutter/material.dart';

import '../core/app_flavor.dart';

/// Root widget shared by both entrypoints. The [flavor] selects admin vs client.
/// Scaffold placeholder only — themed shell, routing and shared widgets land in feature 7.
class CourtlyApp extends StatelessWidget {
  const CourtlyApp({super.key, required this.flavor});

  final AppFlavor flavor;

  String get _title => flavor == AppFlavor.admin ? 'Courtly Admin' : 'Courtly';

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: _title,
      debugShowCheckedModeBanner: false,
      theme: ThemeData(useMaterial3: true, colorSchemeSeed: Colors.blue),
      home: Scaffold(
        appBar: AppBar(title: Text(_title)),
        body: Center(child: Text('${flavor.name} placeholder')),
      ),
    );
  }
}
