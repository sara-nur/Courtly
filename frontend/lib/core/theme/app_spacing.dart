import 'package:flutter/widgets.dart';

/// Single source of spacing, radius and sizing tokens used across both apps.
/// Keeps layouts consistent and avoids magic numbers in widgets (rubric §3.4).
abstract final class AppSpacing {
  // Spacing scale (logical pixels).
  static const double xxs = 4;
  static const double xs = 8;
  static const double sm = 12;
  static const double md = 16;
  static const double lg = 24;
  static const double xl = 32;
  static const double xxl = 48;

  // Corner radii.
  static const double radiusSm = 8;
  static const double radiusMd = 12;
  static const double radiusLg = 16;
  static const double radiusPill = 999;

  // Common component sizes.
  static const double inputHeight = 48;
  static const double topNavHeight = 64;
  static const double avatarSm = 32;
  static const double avatarMd = 40;
  static const double entityThumb = 48;

  // Page content max width (keeps desktop admin readable on wide screens).
  static const double contentMaxWidth = 1200;

  // Reusable rounded shapes.
  static const BorderRadius brSm = BorderRadius.all(Radius.circular(radiusSm));
  static const BorderRadius brMd = BorderRadius.all(Radius.circular(radiusMd));
  static const BorderRadius brLg = BorderRadius.all(Radius.circular(radiusLg));
  static const BorderRadius brPill =
      BorderRadius.all(Radius.circular(radiusPill));

  // Frequently used edge insets.
  static const EdgeInsets pagePadding = EdgeInsets.all(lg);
  static const EdgeInsets cardPadding = EdgeInsets.all(md);
  static const EdgeInsets gapMd = EdgeInsets.all(md);
}
