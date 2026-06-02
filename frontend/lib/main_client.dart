import 'package:flutter/material.dart';

import 'app/app.dart';
import 'core/app_flavor.dart';

/// Mobile client entrypoint (Android). Stripe init lands in feature 26.
void main() {
  runApp(const CourtlyApp(flavor: AppFlavor.client));
}
