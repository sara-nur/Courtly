import '../../../core/enums/reservation_status.dart';

/// Immutable filter set for the Reservations report (date range / court / status). All optional — an omitted date
/// range defaults to the last 30 days on the server, an omitted court/status is not applied. The range spans the slot
/// start time ([fromUtc] inclusive, [toUtc] exclusive). [key] gives a stable identity used to re-key the PDF preview so
/// it refetches when any filter changes.
class ReservationsReportFilters {
  const ReservationsReportFilters({this.fromUtc, this.toUtc, this.courtId, this.status});

  final DateTime? fromUtc;
  final DateTime? toUtc;
  final int? courtId;
  final ReservationStatus? status;

  bool get isActive => fromUtc != null || toUtc != null || courtId != null || status != null;

  ReservationsReportFilters copyWith({
    DateTime? fromUtc,
    bool clearFrom = false,
    DateTime? toUtc,
    bool clearTo = false,
    int? courtId,
    bool clearCourt = false,
    ReservationStatus? status,
    bool clearStatus = false,
  }) =>
      ReservationsReportFilters(
        fromUtc: clearFrom ? null : (fromUtc ?? this.fromUtc),
        toUtc: clearTo ? null : (toUtc ?? this.toUtc),
        courtId: clearCourt ? null : (courtId ?? this.courtId),
        status: clearStatus ? null : (status ?? this.status),
      );

  @override
  bool operator ==(Object other) =>
      other is ReservationsReportFilters &&
      other.fromUtc == fromUtc &&
      other.toUtc == toUtc &&
      other.courtId == courtId &&
      other.status == status;

  @override
  int get hashCode => Object.hash(fromUtc, toUtc, courtId, status);
}

/// Immutable filter set for the Revenue & court-utilisation report (a [month] + an optional [courtId]). [month] is the
/// first day of the chosen month (local); [year]/[monthNumber] expose the parts the API needs.
class RevenueReportFilters {
  const RevenueReportFilters({required this.month, this.courtId});

  final DateTime month;
  final int? courtId;

  int get year => month.year;
  int get monthNumber => month.month;

  RevenueReportFilters copyWith({DateTime? month, int? courtId, bool clearCourt = false}) =>
      RevenueReportFilters(
        month: month ?? this.month,
        courtId: clearCourt ? null : (courtId ?? this.courtId),
      );

  @override
  bool operator ==(Object other) =>
      other is RevenueReportFilters &&
      other.month == month &&
      other.courtId == courtId;

  @override
  int get hashCode => Object.hash(month, courtId);
}
