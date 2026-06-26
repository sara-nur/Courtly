/// Immutable domain models for Feature 15 (reservation management admin UI).
///
/// Mirror the backend reservation DTOs (camelCase JSON keys). The status and
/// bucket arrive as integer enum values **and** as resolved name strings; we
/// parse the enums (`ReservationStatus`/`PaymentStatus`/`TimeOfDayBucket`) and
/// render via their `.label`/`.tone`, so the UI never maps a raw enum or shows a
/// raw DB id. FK ids (court/slot/user) travel with the payload for API calls but
/// the UI renders names — a [Reservation] surfaces [courtName]/[userName] and a
/// formatted [reference] (`#RES-001`), never a bare primary key (rubric §6).
library;

import '../../../core/enums/payment_status.dart';
import '../../../core/enums/reservation_status.dart';
import '../../../core/enums/time_of_day_bucket.dart';

/// One reservation row (the list shape). Times arrive UTC; render with
/// `.toLocal()` via `Formatters`.
class Reservation {
  const Reservation({
    required this.id,
    required this.userId,
    required this.userName,
    this.userEmail,
    required this.courtId,
    required this.courtName,
    required this.timeSlotId,
    required this.slotStartUtc,
    required this.slotEndUtc,
    required this.bucket,
    required this.status,
    required this.totalPrice,
    required this.isPaid,
    required this.createdAtUtc,
    this.cancelledAtUtc,
    this.cancellationReason,
    this.holdExpiresAtUtc,
  });

  final int id;
  final String userId;
  final String userName;
  final String? userEmail;
  final int courtId;
  final String courtName;
  final int timeSlotId;
  final DateTime slotStartUtc;
  final DateTime slotEndUtc;
  final TimeOfDayBucket bucket;
  final ReservationStatus status;

  /// Backend `decimal` → arrives as a JSON number; render with `Formatters.money`.
  final double totalPrice;
  final bool isPaid;
  final DateTime createdAtUtc;
  final DateTime? cancelledAtUtc;
  final String? cancellationReason;
  final DateTime? holdExpiresAtUtc;

  /// Human booking reference shown in the table (`#RES-001`) — a formatted view
  /// of the id, never a bare DB id (rubric §6: forms must not show DB ids).
  String get reference => '#RES-${id.toString().padLeft(3, '0')}';

  /// True while the reservation still holds its slot (Pending/Confirmed).
  bool get isActive =>
      status == ReservationStatus.pending || status == ReservationStatus.confirmed;

  factory Reservation.fromJson(Map<String, dynamic> json) => Reservation(
        id: (json['id'] as num).toInt(),
        userId: json['userId'] as String? ?? '',
        userName: json['userName'] as String? ?? '',
        userEmail: json['userEmail'] as String?,
        courtId: (json['courtId'] as num).toInt(),
        courtName: json['courtName'] as String? ?? '',
        timeSlotId: (json['timeSlotId'] as num).toInt(),
        slotStartUtc: DateTime.parse(json['slotStartUtc'] as String),
        slotEndUtc: DateTime.parse(json['slotEndUtc'] as String),
        bucket: TimeOfDayBucket.fromWire((json['bucket'] as num?)?.toInt() ?? 0),
        status: ReservationStatus.fromWire((json['status'] as num?)?.toInt() ?? 0),
        totalPrice: (json['totalPrice'] as num?)?.toDouble() ?? 0,
        isPaid: json['isPaid'] as bool? ?? false,
        createdAtUtc: DateTime.parse(json['createdAtUtc'] as String),
        cancelledAtUtc: json['cancelledAtUtc'] == null
            ? null
            : DateTime.parse(json['cancelledAtUtc'] as String),
        cancellationReason: json['cancellationReason'] as String?,
        holdExpiresAtUtc: json['holdExpiresAtUtc'] == null
            ? null
            : DateTime.parse(json['holdExpiresAtUtc'] as String),
      );
}

/// One entry in a reservation's audit trail — who moved it to [newStatus], when,
/// and why. [changedByName] is the resolved actor (null when system-made). A
/// reschedule records [oldStatus] == [newStatus] with the move in [reason].
class ReservationAudit {
  const ReservationAudit({
    required this.id,
    this.oldStatus,
    required this.newStatus,
    this.reason,
    this.changedByName,
    required this.createdAtUtc,
  });

  final int id;
  final ReservationStatus? oldStatus;
  final ReservationStatus newStatus;
  final String? reason;
  final String? changedByName;
  final DateTime createdAtUtc;

  /// True for a reschedule audit (status unchanged; the move is in [reason]).
  bool get isReschedule => oldStatus != null && oldStatus == newStatus;

  factory ReservationAudit.fromJson(Map<String, dynamic> json) => ReservationAudit(
        id: (json['id'] as num).toInt(),
        oldStatus: json['oldStatus'] == null
            ? null
            : ReservationStatus.fromWire((json['oldStatus'] as num).toInt()),
        newStatus: ReservationStatus.fromWire((json['newStatus'] as num?)?.toInt() ?? 0),
        reason: json['reason'] as String?,
        changedByName: json['changedByName'] as String?,
        createdAtUtc: DateTime.parse(json['createdAtUtc'] as String),
      );
}

/// The reservation's payment summary (feature 16 fills the lifecycle; F15 only
/// reads it — to show the paid state and gate cancel/reschedule).
class ReservationPayment {
  const ReservationPayment({
    required this.id,
    required this.status,
    required this.amount,
    this.amountChargedCents,
    required this.isPaid,
    required this.createdAtUtc,
    this.paidAtUtc,
  });

  final int id;
  final PaymentStatus status;
  final double amount;
  final int? amountChargedCents;
  final bool isPaid;
  final DateTime createdAtUtc;
  final DateTime? paidAtUtc;

  factory ReservationPayment.fromJson(Map<String, dynamic> json) => ReservationPayment(
        id: (json['id'] as num).toInt(),
        status: PaymentStatus.fromWire((json['status'] as num?)?.toInt() ?? 0),
        amount: (json['amount'] as num?)?.toDouble() ?? 0,
        amountChargedCents: (json['amountChargedCents'] as num?)?.toInt(),
        isPaid: json['isPaid'] as bool? ?? false,
        createdAtUtc: DateTime.parse(json['createdAtUtc'] as String),
        paidAtUtc: json['paidAtUtc'] == null
            ? null
            : DateTime.parse(json['paidAtUtc'] as String),
      );
}

/// The full reservation view for the master-detail screen: the reservation, its
/// audit trail (oldest-first) and the payment summary (null when unpaid).
class ReservationDetail {
  const ReservationDetail({
    required this.reservation,
    required this.audits,
    this.payment,
  });

  final Reservation reservation;
  final List<ReservationAudit> audits;
  final ReservationPayment? payment;

  factory ReservationDetail.fromJson(Map<String, dynamic> json) => ReservationDetail(
        reservation:
            Reservation.fromJson((json['reservation'] as Map).cast<String, dynamic>()),
        audits: ((json['audits'] as List?) ?? const [])
            .map((e) => ReservationAudit.fromJson((e as Map).cast<String, dynamic>()))
            .toList(growable: false),
        payment: json['payment'] == null
            ? null
            : ReservationPayment.fromJson((json['payment'] as Map).cast<String, dynamic>()),
      );
}
