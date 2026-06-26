using Courtly.Domain.Enums;

namespace Courtly.Contracts.Reservations;

/// <summary>
/// Reservation engine contracts (feature 14). A reservation books one <c>TimeSlot</c>; its <see cref="Status"/> moves
/// only through the centralized reservation state machine (Pending → Confirmed → Completed/Cancelled). DTOs only on the
/// wire — entities never leave the service.
/// </summary>
/// <remarks>
/// <see cref="ReservationDto"/> is the LIST shape (the resolved court/user names are JOINed, never raw — though the FK
/// ids ride along for the client to act on). <see cref="StatusName"/>/<see cref="BucketName"/> are the human labels so
/// the client never maps the raw enums; <see cref="IsPaid"/> is true only when a <c>Succeeded</c> payment exists (it
/// hides the pay button later, rubric §7.1). <see cref="ReservationDetailDto"/> adds the full audit trail and the
/// payment summary for the admin master-detail (feature 15) and the client booking detail (feature 26A). The server
/// owns the price (the create request carries only the slot id — never a price or a court id, both derived
/// server-side).
/// </remarks>
public sealed record ReservationDto(
    long Id,
    Guid UserId,
    string UserName,
    string? UserEmail,
    long CourtId,
    string CourtName,
    long TimeSlotId,
    DateTime SlotStartUtc,
    DateTime SlotEndUtc,
    TimeOfDayBucket Bucket,
    string BucketName,
    ReservationStatus Status,
    string StatusName,
    decimal TotalPrice,
    bool IsPaid,
    DateTime CreatedAtUtc,
    DateTime? CancelledAtUtc,
    string? CancellationReason,
    DateTime? HoldExpiresAtUtc);

/// <summary>One entry in a reservation's audit trail — who moved it to <see cref="NewStatusName"/>, when, and why
/// (rubric §7: audit of who approved/refused, when, with a description). <see cref="ChangedByName"/> is the resolved
/// actor (JOINed, never the raw id); null when the move was made by the system (e.g. an automatic completion).</summary>
public sealed record ReservationAuditDto(
    long Id,
    ReservationStatus? OldStatus,
    string? OldStatusName,
    ReservationStatus NewStatus,
    string NewStatusName,
    string? Reason,
    string? ChangedByName,
    DateTime CreatedAtUtc);

/// <summary>The reservation's payment summary (feature 16 fills the lifecycle; F14 only reads it). <see cref="IsPaid"/>
/// is true when <see cref="Status"/> is <c>Succeeded</c>.</summary>
public sealed record ReservationPaymentDto(
    long Id,
    PaymentStatus Status,
    string StatusName,
    decimal Amount,
    long? AmountChargedCents,
    bool IsPaid,
    DateTime CreatedAtUtc,
    DateTime? PaidAtUtc);

/// <summary>The full reservation view for the master-detail screens: the reservation, its audit trail (oldest-first),
/// and the payment summary (null when unpaid).</summary>
public sealed record ReservationDetailDto(
    ReservationDto Reservation,
    IReadOnlyList<ReservationAuditDto> Audits,
    ReservationPaymentDto? Payment);

/// <summary>Books a slot. The client sends only the slot id; the server derives the court, owns the price, and takes
/// the owner from the JWT (rubric §5/§7.1 — never trust the client for owner, court or amount).</summary>
public sealed record CreateReservationRequest(long TimeSlotId);

/// <summary>Cancels a reservation. A <see cref="Reason"/> is required (rubric §7: a cancellation/refusal must carry a
/// reason and raise a notification).</summary>
public sealed record CancelReservationRequest(string Reason);

/// <summary>
/// Query-string filters for the reservation lists (bound via <c>[FromQuery]</c>, applied at the database). All
/// nullable — an omitted filter is not applied. The admin list (feature 15) uses every filter; the client "my
/// reservations" list forces the owner to the caller and uses only <see cref="Status"/> + the date range, which spans
/// the slot start time (<see cref="FromUtc"/> inclusive, <see cref="ToUtc"/> exclusive).
/// </summary>
public sealed record ReservationListQuery(
    ReservationStatus? Status = null,
    long? CourtId = null,
    Guid? UserId = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null);
