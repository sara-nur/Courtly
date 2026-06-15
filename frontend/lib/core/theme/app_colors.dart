import 'package:flutter/material.dart';

/// Single source of truth for the Courtly palette (rubric §3.4 — no magic
/// colors scattered across widgets). Clean, light surfaces with a royal-blue
/// accent, matching `ui_design_and_scope.pdf`.
abstract final class AppColors {
  // Brand / accent.
  static const Color primary = Color(0xFF2563EB); // royal blue
  static const Color primaryDark = Color(0xFF1D4ED8);
  static const Color primaryLight = Color(0xFF3B82F6);
  // ~12% primary tint, precomputed as ARGB so it's a const (no version-specific
  // Color.withValues / withOpacity call). Used for active-tab / indicator fills.
  static const Color primarySoft = Color(0x1F2563EB);
  static const Color logoAccent = Color(0xFF38BDF8); // cyan swoosh in the logo

  // Neutral surfaces.
  static const Color background = Color(0xFFF8FAFC); // slate-50
  static const Color surface = Color(0xFFFFFFFF);
  static const Color surfaceMuted = Color(0xFFF1F5F9); // slate-100
  static const Color border = Color(0xFFE2E8F0); // slate-200

  // Text.
  static const Color textPrimary = Color(0xFF0F172A); // slate-900
  static const Color textSecondary = Color(0xFF475569); // slate-600
  static const Color textMuted = Color(0xFF94A3B8); // slate-400
  static const Color onPrimary = Color(0xFFFFFFFF);

  // Semantic base colors.
  static const Color success = Color(0xFF16A34A);
  static const Color warning = Color(0xFFD97706);
  static const Color danger = Color(0xFFDC2626);
  static const Color info = primary;
}

/// Semantic tone for a [StatusBadge]. Status enums map to one of these so the
/// badge colors stay consistent everywhere (Confirmed/Available = success,
/// Pending/Occupied = warning, Cancelled/Maintenance = danger, etc.).
enum StatusTone { success, warning, danger, info, neutral }

/// Foreground + background pair for a [StatusTone] (light pill, darker text/dot).
class StatusToneColors {
  const StatusToneColors({required this.foreground, required this.background});
  final Color foreground;
  final Color background;

  static StatusToneColors of(StatusTone tone) {
    switch (tone) {
      case StatusTone.success:
        return const StatusToneColors(
          foreground: Color(0xFF15803D),
          background: Color(0xFFDCFCE7),
        );
      case StatusTone.warning:
        return const StatusToneColors(
          foreground: Color(0xFFB45309),
          background: Color(0xFFFEF3C7),
        );
      case StatusTone.danger:
        return const StatusToneColors(
          foreground: Color(0xFFB91C1C),
          background: Color(0xFFFEE2E2),
        );
      case StatusTone.info:
        return const StatusToneColors(
          foreground: Color(0xFF1D4ED8),
          background: Color(0xFFDBEAFE),
        );
      case StatusTone.neutral:
        return const StatusToneColors(
          foreground: Color(0xFF475569),
          background: Color(0xFFF1F5F9),
        );
    }
  }
}
