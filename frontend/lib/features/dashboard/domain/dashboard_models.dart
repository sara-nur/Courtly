// Dashboard analytics models (Feature 19) — manual `fromJson` mirrors of the
// backend `DashboardMetricsDto` (camelCase keys, enums as ints on the wire, no
// codegen). Money values arrive in currency units (not cents); counts and
// occupancy ride in the same Kpi.current double and are formatted by the UI.

/// Overall business-health signal for the dashboard banner. Mirrors the backend
/// `DashboardHealthStatus` enum (serialized as its int value).
enum DashboardHealthStatus {
  healthy,
  watch,
  atRisk;

  static DashboardHealthStatus fromWire(int value) => switch (value) {
        0 => DashboardHealthStatus.healthy,
        1 => DashboardHealthStatus.watch,
        _ => DashboardHealthStatus.atRisk,
      };
}

/// A single KPI: its value for the window, the same metric over the prior
/// equal-length window, and the % change. [deltaPercent] is null when the prior
/// value was zero and the current is not (a growth-from-zero shown as "new").
class Kpi {
  const Kpi({required this.current, required this.previous, this.deltaPercent});

  final double current;
  final double previous;
  final double? deltaPercent;

  /// True when there is a meaningful delta to render an arrow for.
  bool get hasDelta => deltaPercent != null;

  /// True when the metric grew (or held flat) versus the prior period.
  bool get isUp => (deltaPercent ?? 0) >= 0;

  factory Kpi.fromJson(Map<String, dynamic> json) => Kpi(
        current: (json['current'] as num?)?.toDouble() ?? 0,
        previous: (json['previous'] as num?)?.toDouble() ?? 0,
        deltaPercent: (json['deltaPercent'] as num?)?.toDouble(),
      );
}

/// One point on the Revenue Trends line chart — net revenue on a UTC day.
class RevenueTrendPoint {
  const RevenueTrendPoint({required this.dateUtc, required this.amount});

  final DateTime dateUtc;
  final double amount;

  factory RevenueTrendPoint.fromJson(Map<String, dynamic> json) =>
      RevenueTrendPoint(
        dateUtc: DateTime.parse(json['dateUtc'] as String),
        amount: (json['amount'] as num?)?.toDouble() ?? 0,
      );
}

/// One bar on the Most Popular Courts chart — a court by name + image (never id),
/// its counted-reservation total, and that as a percentage of the window's total.
class PopularCourt {
  const PopularCourt({
    required this.courtName,
    this.imageUrl,
    required this.count,
    required this.percentage,
  });

  final String courtName;

  /// Relative `/api/images/{id}` (absolutized at render time), or null.
  final String? imageUrl;
  final int count;
  final double percentage;

  factory PopularCourt.fromJson(Map<String, dynamic> json) => PopularCourt(
        courtName: json['courtName'] as String? ?? '',
        imageUrl: json['imageUrl'] as String?,
        count: (json['count'] as num?)?.toInt() ?? 0,
        percentage: (json['percentage'] as num?)?.toDouble() ?? 0,
      );
}

/// One point on the Peak Hours line chart — counted reservations starting in
/// [hour] (0–23, UTC). The series always carries all 24 hours.
class PeakHourPoint {
  const PeakHourPoint({required this.hour, required this.count});

  final int hour;
  final int count;

  factory PeakHourPoint.fromJson(Map<String, dynamic> json) => PeakHourPoint(
        hour: (json['hour'] as num?)?.toInt() ?? 0,
        count: (json['count'] as num?)?.toInt() ?? 0,
      );
}

/// The "Business Health Check" banner — a derived status plus a short message.
class HealthCheck {
  const HealthCheck({
    required this.status,
    required this.statusName,
    required this.headline,
    required this.detail,
  });

  final DashboardHealthStatus status;
  final String statusName;
  final String headline;
  final String detail;

  factory HealthCheck.fromJson(Map<String, dynamic> json) => HealthCheck(
        status: DashboardHealthStatus.fromWire((json['status'] as num?)?.toInt() ?? 0),
        statusName: json['statusName'] as String? ?? '',
        headline: json['headline'] as String? ?? '',
        detail: json['detail'] as String? ?? '',
      );
}

/// The full dashboard payload: the resolved window, 4 KPIs, three chart series,
/// and the health banner — everything one `GET /api/dashboard/metrics` returns.
class DashboardMetrics {
  const DashboardMetrics({
    required this.fromUtc,
    required this.toUtc,
    required this.totalReservations,
    required this.revenue,
    required this.occupancyRate,
    required this.activeUsers,
    required this.revenueTrend,
    required this.popularCourts,
    required this.peakHours,
    required this.health,
  });

  final DateTime fromUtc;
  final DateTime toUtc;
  final Kpi totalReservations;
  final Kpi revenue;
  final Kpi occupancyRate;
  final Kpi activeUsers;
  final List<RevenueTrendPoint> revenueTrend;
  final List<PopularCourt> popularCourts;
  final List<PeakHourPoint> peakHours;
  final HealthCheck health;

  factory DashboardMetrics.fromJson(Map<String, dynamic> json) {
    List<T> listOf<T>(String key, T Function(Map<String, dynamic>) build) =>
        ((json[key] as List?) ?? const <dynamic>[])
            .map((e) => build((e as Map).cast<String, dynamic>()))
            .toList(growable: false);

    return DashboardMetrics(
      fromUtc: DateTime.parse(json['fromUtc'] as String),
      toUtc: DateTime.parse(json['toUtc'] as String),
      totalReservations: Kpi.fromJson((json['totalReservations'] as Map).cast<String, dynamic>()),
      revenue: Kpi.fromJson((json['revenue'] as Map).cast<String, dynamic>()),
      occupancyRate: Kpi.fromJson((json['occupancyRate'] as Map).cast<String, dynamic>()),
      activeUsers: Kpi.fromJson((json['activeUsers'] as Map).cast<String, dynamic>()),
      revenueTrend: listOf('revenueTrend', RevenueTrendPoint.fromJson),
      popularCourts: listOf('popularCourts', PopularCourt.fromJson),
      peakHours: listOf('peakHours', PeakHourPoint.fromJson),
      health: HealthCheck.fromJson((json['health'] as Map).cast<String, dynamic>()),
    );
  }
}
