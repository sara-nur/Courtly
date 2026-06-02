import 'package:flutter/material.dart';

import 'app/app.dart';
import 'core/app_flavor.dart';

/// Desktop admin entrypoint (macOS dev / Windows submission).
void main() {
  runApp(const CourtlyApp(flavor: AppFlavor.admin));
}
