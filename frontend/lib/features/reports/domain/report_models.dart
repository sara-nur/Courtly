/// Client models for the feature-20 report data (the JSON the `/api/reports/*/data` endpoints return). The PDF
/// endpoints return bytes for the preview/print path; these models back the in-app table view. Money values are in
/// catalog currency units (the server already divided cents by 100).
library;

/// One row of the reservations report.
class ReservationReportRow {
  const ReservationReportRow({
    required this.reference,
    required this.customer,
    required this.email,
    required this.courtName,
    required this.slotStartUtc,
    required this.slotEndUtc,
    required this.statusName,
    required this.amount,
    required this.isPaid,
  });

  final String reference;
  final String customer;
  final String? email;
  final String courtName;
  final DateTime slotStartUtc;
  final DateTime slotEndUtc;
  final String statusName;
  final double amount;
  final bool isPaid;

  factory ReservationReportRow.fromJson(Map<String, dynamic> json) => ReservationReportRow(
        reference: json['reference'] as String,
        customer: json['customer'] as String,
        email: json['email'] as String?,
        courtName: json['courtName'] as String,
        slotStartUtc: DateTime.parse(json['slotStartUtc'] as String).toUtc(),
        slotEndUtc: DateTime.parse(json['slotEndUtc'] as String).toUtc(),
        statusName: json['statusName'] as String,
        amount: (json['amount'] as num).toDouble(),
        isPaid: json['isPaid'] as bool,
      );
}

/// A status total in the reservations report summary.
class ReportStatusCount {
  const ReportStatusCount({required this.statusName, required this.count});

  final String statusName;
  final int count;

  factory ReportStatusCount.fromJson(Map<String, dynamic> json) => ReportStatusCount(
        statusName: json['statusName'] as String,
        count: (json['count'] as num).toInt(),
      );
}

/// The full reservations report payload.
class ReservationsReportData {
  const ReservationsReportData({
    required this.fromUtc,
    required this.toUtc,
    required this.courtLabel,
    required this.statusLabel,
    required this.rows,
    required this.statusBreakdown,
    required this.totalCount,
    required this.totalAmount,
    required this.generatedAtUtc,
  });

  final DateTime fromUtc;
  final DateTime toUtc;
  final String courtLabel;
  final String statusLabel;
  final List<ReservationReportRow> rows;
  final List<ReportStatusCount> statusBreakdown;
  final int totalCount;
  final double totalAmount;
  final DateTime generatedAtUtc;

  factory ReservationsReportData.fromJson(Map<String, dynamic> json) => ReservationsReportData(
        fromUtc: DateTime.parse(json['fromUtc'] as String).toUtc(),
        toUtc: DateTime.parse(json['toUtc'] as String).toUtc(),
        courtLabel: json['courtLabel'] as String,
        statusLabel: json['statusLabel'] as String,
        rows: (json['rows'] as List<dynamic>)
            .map((e) => ReservationReportRow.fromJson((e as Map).cast<String, dynamic>()))
            .toList(),
        statusBreakdown: (json['statusBreakdown'] as List<dynamic>)
            .map((e) => ReportStatusCount.fromJson((e as Map).cast<String, dynamic>()))
            .toList(),
        totalCount: (json['totalCount'] as num).toInt(),
        totalAmount: (json['totalAmount'] as num).toDouble(),
        generatedAtUtc: DateTime.parse(json['generatedAtUtc'] as String).toUtc(),
      );
}

/// One court's line in the revenue & utilisation report.
class CourtUtilizationRow {
  const CourtUtilizationRow({
    required this.courtName,
    required this.bookings,
    required this.revenue,
    required this.utilizationPct,
  });

  final String courtName;
  final int bookings;
  final double revenue;
  final double utilizationPct;

  factory CourtUtilizationRow.fromJson(Map<String, dynamic> json) => CourtUtilizationRow(
        courtName: json['courtName'] as String,
        bookings: (json['bookings'] as num).toInt(),
        revenue: (json['revenue'] as num).toDouble(),
        utilizationPct: (json['utilizationPct'] as num).toDouble(),
      );
}

/// The full revenue & court-utilisation report payload.
class RevenueUtilizationReportData {
  const RevenueUtilizationReportData({
    required this.year,
    required this.month,
    required this.monthLabel,
    required this.courtLabel,
    required this.rows,
    required this.totalBookings,
    required this.totalRevenue,
    required this.overallUtilizationPct,
    required this.generatedAtUtc,
  });

  final int year;
  final int month;
  final String monthLabel;
  final String courtLabel;
  final List<CourtUtilizationRow> rows;
  final int totalBookings;
  final double totalRevenue;
  final double overallUtilizationPct;
  final DateTime generatedAtUtc;

  factory RevenueUtilizationReportData.fromJson(Map<String, dynamic> json) => RevenueUtilizationReportData(
        year: (json['year'] as num).toInt(),
        month: (json['month'] as num).toInt(),
        monthLabel: json['monthLabel'] as String,
        courtLabel: json['courtLabel'] as String,
        rows: (json['rows'] as List<dynamic>)
            .map((e) => CourtUtilizationRow.fromJson((e as Map).cast<String, dynamic>()))
            .toList(),
        totalBookings: (json['totalBookings'] as num).toInt(),
        totalRevenue: (json['totalRevenue'] as num).toDouble(),
        overallUtilizationPct: (json['overallUtilizationPct'] as num).toDouble(),
        generatedAtUtc: DateTime.parse(json['generatedAtUtc'] as String).toUtc(),
      );
}
