using Courtly.Application.Abstractions;
using Courtly.Application.Common.Exceptions;
using Courtly.Application.Common.Pagination;
using Courtly.Application.Courts.Maintenance;
using Courtly.Contracts.Common;
using Courtly.Contracts.Messaging;
using Courtly.Contracts.Reservations;
using Courtly.Domain.Constants;
using Courtly.Domain.Entities;
using Courtly.Domain.Enums;
using Courtly.Infrastructure.Configuration;
using Courtly.Infrastructure.Messaging;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Courtly.Application.Reservations;

/// <summary>
/// The reservation engine (feature 14). Creates a booking and drives it through <see cref="ReservationStateMachine"/>;
/// every transition writes a <see cref="ReservationAudit"/> row and publishes a <see cref="ReservationEvent"/> — the
/// transition rules are never decided in the controller (rubric §7). The owner always comes from the JWT via
/// <see cref="ICurrentUser"/> (never the route/body), every timestamp from <see cref="IClock"/> (UTC), and reads are
/// <c>AsNoTracking</c>, projected to DTOs (never entities).
/// </summary>
/// <remarks>
/// <para><b>Server-authoritative.</b> The create request carries only a slot id; the court is derived from the slot and
/// the price is the slot's server-owned <c>Price</c> — the client is never trusted for owner, court or amount
/// (rubric §7.1).</para>
/// <para><b>Overlap defense (two layers).</b> A service pre-check rejects a slot that already has an active
/// (Pending/Confirmed) reservation; behind it, the filtered-unique index <c>ux_reservations_active_timeslot</c> is the
/// hard guard for the concurrent race — a unique violation on insert is translated to a friendly 409, never a 500
/// (build-plan §13).</para>
/// <para><b>Pricing edge cases.</b> The total is the slot's price, which the slot catalog (feature 13) derives from the
/// court rate × duration — so a 25h/48h slot is priced correctly by construction (rubric §7), and F14 never recomputes
/// or trusts a client amount.</para>
/// <para><b>Single SaveChanges per transition.</b> Each operation persists the status change + its audit row in one
/// <c>SaveChangesAsync</c> (atomic), so no explicit transaction is required (rubric §3.4); the event is published only
/// after that commit succeeds.</para>
/// </remarks>
public sealed class ReservationService : IReservationService
{
    private readonly CourtlyDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IMaintenanceService _maintenance;
    private readonly IReservationEventPublisher _events;
    private readonly ILogger<ReservationService> _logger;
    private readonly int _holdMinutes;

    public ReservationService(
        CourtlyDbContext db,
        IClock clock,
        ICurrentUser currentUser,
        IMaintenanceService maintenance,
        IReservationEventPublisher events,
        IOptions<ReservationOptions> options,
        ILogger<ReservationService> logger)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _maintenance = maintenance;
        _events = events;
        _logger = logger;
        _holdMinutes = Math.Max(1, options.Value.HoldMinutes); // read env once in ctor (rubric §8.2)
    }

    public Task<ReservationDetailDto> CreateAsync(CreateReservationRequest request, CancellationToken ct = default) =>
        // The client books for itself — the owner is the caller (rubric §5: never from the route/body).
        CreateCoreAsync(request.TimeSlotId, CurrentUserId(), ct);

    public async Task<ReservationDetailDto> CreateForUserAsync(
        AdminCreateReservationRequest request, CancellationToken ct = default)
    {
        // Admin/staff manual booking on a customer's behalf (rubric §5 allows acting on other users' data). Validate
        // the target user exists and is active; the acting admin is still recorded as the audit actor (NewAudit reads
        // ICurrentUser, which is the admin here).
        var owner = await _db.Users.AsNoTracking()
            .Where(u => u.Id == request.UserId)
            .Select(u => new { u.Id, u.IsActive })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException($"User {request.UserId} was not found.");

        if (!owner.IsActive)
        {
            throw new BusinessException("This user is deactivated; a booking cannot be created for them.");
        }

        return await CreateCoreAsync(request.TimeSlotId, owner.Id, ct);
    }

    /// <summary>The shared create path used by both the client booking (<see cref="CreateAsync"/>) and the admin
    /// manual booking (<see cref="CreateForUserAsync"/>): loads the slot + its court, runs the server-side
    /// preconditions (active/past/maintenance/overlap), inserts the Pending reservation + its first audit row in one
    /// SaveChanges, and publishes <c>reservation.created</c>. <paramref name="ownerUserId"/> is the booking's owner;
    /// the audit actor is always the current user.</summary>
    private async Task<ReservationDetailDto> CreateCoreAsync(long timeSlotId, Guid ownerUserId, CancellationToken ct)
    {
        var now = _clock.UtcNow;

        // The slot + its court — the server derives the court from the slot (never a client-supplied court id).
        var slot = await _db.TimeSlots.AsNoTracking()
            .Where(s => s.Id == timeSlotId)
            .Select(s => new
            {
                s.Id,
                s.CourtId,
                s.StartUtc,
                s.EndUtc,
                s.Price,
                SlotActive = s.IsActive,
                CourtActive = s.Court.IsActive,
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException($"Time slot {timeSlotId} was not found.");

        if (!slot.SlotActive)
        {
            throw new BusinessException("This time slot is no longer available for booking.");
        }

        if (!slot.CourtActive)
        {
            throw new BusinessException("This court is not currently available for booking.");
        }

        if (slot.StartUtc <= now)
        {
            throw new BusinessException("This time slot is in the past and can no longer be booked.");
        }

        // Court under maintenance for the slot window → not bookable (feature 12 exclusion, reused).
        var underMaintenance = await _maintenance.GetCourtIdsUnderMaintenanceAsync(slot.StartUtc, slot.EndUtc, ct);
        if (underMaintenance.Contains(slot.CourtId))
        {
            throw new BusinessException(
                "This court is under maintenance for the selected time and can't be booked.");
        }

        // Overlap pre-check (unit-testable; the filtered-unique index is the hard guard for the concurrent race).
        if (await ActiveReservationExistsAsync(slot.Id, excludeReservationId: 0, ct))
        {
            throw new ConflictException("This slot was just taken. Please pick another time.");
        }

        var reservation = new Reservation
        {
            UserId = ownerUserId,
            CourtId = slot.CourtId,
            TimeSlotId = slot.Id,
            Status = ReservationStatus.Pending,
            TotalPrice = slot.Price, // server-owned price (rubric §7.1)
            CreatedAtUtc = now,
            HoldExpiresAtUtc = now.AddMinutes(_holdMinutes), // unpaid hold (Worker releases it in F17)
        };
        reservation.Audits.Add(NewAudit(null, ReservationStatus.Pending, reason: null, now));
        _db.Reservations.Add(reservation);

        try
        {
            // One SaveChanges commits the reservation + its first audit row atomically.
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost the concurrent race: the filtered-unique index rejected a 2nd active booking of this slot.
            // Disambiguate by re-reading — an active reservation now present ⇒ friendly 409 (never a 500).
            if (await ActiveReservationExistsAsync(slot.Id, excludeReservationId: reservation.Id, ct))
            {
                throw new ConflictException("This slot was just taken. Please pick another time.");
            }

            throw;
        }

        await PublishAsync(ReservationRoutingKeys.Created, reservation, slot.StartUtc, slot.EndUtc, reason: null, ct);
        _logger.LogInformation(
            "Created reservation {ReservationId} (user {UserId}, court {CourtId}, slot {SlotId}).",
            reservation.Id, ownerUserId, slot.CourtId, slot.Id);

        return await ProjectDetailAsync(reservation.Id, ct);
    }

    public async Task<ReservationDetailDto> ConfirmAsync(long id, CancellationToken ct = default)
    {
        // Defense-in-depth: confirming is an admin/staff operation. The controller is role-gated, but the service must
        // not assume it (a future endpoint, job, or test utility could call it directly) — same posture as CancelAsync.
        EnsureStaffOrAdmin();

        var reservation = await LoadWithSlotAsync(id, ct);
        ReservationStateMachine.EnsureCanTransition(reservation.Status, ReservationStatus.Confirmed);

        var now = _clock.UtcNow;
        var old = reservation.Status;
        reservation.Status = ReservationStatus.Confirmed;
        reservation.Audits.Add(NewAudit(old, ReservationStatus.Confirmed, reason: null, now));
        await _db.SaveChangesAsync(ct);

        await PublishAsync(
            ReservationRoutingKeys.Confirmed, reservation, reservation.TimeSlot.StartUtc, reservation.TimeSlot.EndUtc,
            reason: null, ct);
        _logger.LogInformation("Confirmed reservation {ReservationId}.", id);

        return await ProjectDetailAsync(id, ct);
    }

    public async Task<ReservationDetailDto> CancelAsync(
        long id, CancelReservationRequest request, CancellationToken ct = default)
    {
        var reservation = await _db.Reservations
            .Include(r => r.TimeSlot)
            .Include(r => r.Payment)
            .FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException($"Reservation {id} was not found.");

        // Ownership: a user may cancel their own booking; admin/staff may cancel anyone's (rubric §5).
        var userId = CurrentUserId();
        if (reservation.UserId != userId && !IsStaffOrAdmin())
        {
            throw new ForbiddenException("You can only cancel your own reservations.");
        }

        ReservationStateMachine.EnsureCanTransition(reservation.Status, ReservationStatus.Cancelled);

        // Can't cancel a paid booking here — that needs the Stripe refund flow (rubric §7/§7.1; feature 16).
        if (reservation.Payment is { Status: PaymentStatus.Succeeded })
        {
            throw new BusinessException(
                "This reservation has been paid; cancelling it requires a refund, which is handled by the payment flow.");
        }

        // A payment already in progress must settle first. Cancelling underneath an in-flight Stripe charge risks the
        // "charged but not confirmed" hazard: the webhook could still succeed after the reservation is gone. We block
        // here; the hold-expiry worker releases the reservation, and any late webhook is resolved by the payment flow
        // (confirm on success, or park the payment for review if the reservation is no longer confirmable).
        if (reservation.Payment is { Status: PaymentStatus.Pending })
        {
            throw new BusinessException(
                "A payment is in progress for this reservation; wait for it to complete or the hold to expire before cancelling.");
        }

        var reason = (request.Reason ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(reason))
        {
            throw new ValidationException("A cancellation reason is required.");
        }

        var now = _clock.UtcNow;
        var old = reservation.Status;
        reservation.Status = ReservationStatus.Cancelled;
        reservation.CancelledAtUtc = now;
        reservation.CancellationReason = reason;
        reservation.Audits.Add(NewAudit(old, ReservationStatus.Cancelled, reason, now));
        await _db.SaveChangesAsync(ct);

        // Cancelling frees the slot automatically — the filtered-unique guard only counts active reservations.
        await PublishAsync(
            ReservationRoutingKeys.Cancelled, reservation, reservation.TimeSlot.StartUtc, reservation.TimeSlot.EndUtc,
            reason, ct);
        _logger.LogInformation("Cancelled reservation {ReservationId} (reason: {Reason}).", id, reason);

        return await ProjectDetailAsync(id, ct);
    }

    public async Task<ReservationDetailDto> CompleteAsync(long id, CancellationToken ct = default)
    {
        // Defense-in-depth: completing is an admin/staff operation (the auto-complete worker calls the dedicated
        // ReservationAutoCompleteService, not this method). Don't rely solely on controller route authorization.
        EnsureStaffOrAdmin();

        var reservation = await LoadWithSlotAsync(id, ct);
        ReservationStateMachine.EnsureCanTransition(reservation.Status, ReservationStatus.Completed);

        if (reservation.TimeSlot.EndUtc > _clock.UtcNow)
        {
            throw new BusinessException("A reservation can only be completed after its time slot has ended.");
        }

        var now = _clock.UtcNow;
        var old = reservation.Status;
        reservation.Status = ReservationStatus.Completed;
        reservation.Audits.Add(NewAudit(old, ReservationStatus.Completed, reason: null, now));
        await _db.SaveChangesAsync(ct);

        await PublishAsync(
            ReservationRoutingKeys.Completed, reservation, reservation.TimeSlot.StartUtc, reservation.TimeSlot.EndUtc,
            reason: null, ct);
        _logger.LogInformation("Completed reservation {ReservationId}.", id);

        return await ProjectDetailAsync(id, ct);
    }

    public async Task<ReservationDetailDto> RescheduleAsync(
        long id, RescheduleReservationRequest request, CancellationToken ct = default)
    {
        var reservation = await _db.Reservations
            .Include(r => r.TimeSlot)
            .Include(r => r.Payment)
            .FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException($"Reservation {id} was not found.");

        // Only an active (Pending/Confirmed) reservation holds a slot and can be moved; terminal ones are immutable.
        if (!ReservationStateMachine.IsActive(reservation.Status))
        {
            throw new BusinessException(
                $"A {StatusLabel(reservation.Status).ToLowerInvariant()} reservation can't be rescheduled.");
        }

        // A paid booking would need a payment adjustment to move to a differently-priced slot (feature 16) — block it,
        // mirroring the paid-cancel guard.
        if (reservation.Payment is { Status: PaymentStatus.Succeeded })
        {
            throw new BusinessException(
                "This reservation has been paid; rescheduling it would require a payment adjustment.");
        }

        if (request.NewTimeSlotId == reservation.TimeSlotId)
        {
            throw new BusinessException("This reservation is already booked into that time slot.");
        }

        var now = _clock.UtcNow;

        // The target slot + its court — same server-derived shape as create (never a client-supplied court id).
        var newSlot = await _db.TimeSlots.AsNoTracking()
            .Where(s => s.Id == request.NewTimeSlotId)
            .Select(s => new
            {
                s.Id,
                s.CourtId,
                s.StartUtc,
                s.EndUtc,
                s.Price,
                SlotActive = s.IsActive,
                CourtActive = s.Court.IsActive,
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException($"Time slot {request.NewTimeSlotId} was not found.");

        // Re-run the create preconditions on the target slot (rubric §7: server-side availability + preconditions).
        if (!newSlot.SlotActive)
        {
            throw new BusinessException("This time slot is no longer available for booking.");
        }

        if (!newSlot.CourtActive)
        {
            throw new BusinessException("This court is not currently available for booking.");
        }

        if (newSlot.StartUtc <= now)
        {
            throw new BusinessException("This time slot is in the past and can no longer be booked.");
        }

        var underMaintenance = await _maintenance.GetCourtIdsUnderMaintenanceAsync(newSlot.StartUtc, newSlot.EndUtc, ct);
        if (underMaintenance.Contains(newSlot.CourtId))
        {
            throw new BusinessException(
                "This court is under maintenance for the selected time and can't be booked.");
        }

        // Overlap pre-check on the target slot (excluding this reservation); the filtered-unique index is the hard
        // guard for the concurrent race when the UPDATE moves the row onto the new slot.
        if (await ActiveReservationExistsAsync(newSlot.Id, excludeReservationId: reservation.Id, ct))
        {
            throw new ConflictException("This slot was just taken. Please pick another time.");
        }

        var fromStartUtc = reservation.TimeSlot.StartUtc;
        reservation.TimeSlotId = newSlot.Id;
        reservation.CourtId = newSlot.CourtId; // court is always derived from the slot
        reservation.TotalPrice = newSlot.Price; // re-price from the new slot (server-owned, rubric §7.1)
        var reason =
            $"Rescheduled from {fromStartUtc:yyyy-MM-dd HH:mm} UTC to {newSlot.StartUtc:yyyy-MM-dd HH:mm} UTC.";
        // The status is unchanged by a reschedule, so the audit row records old == new with the move recorded in the
        // reason (the frontend renders an old == new audit as "Rescheduled").
        reservation.Audits.Add(NewAudit(reservation.Status, reservation.Status, reason, now));

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            if (await ActiveReservationExistsAsync(newSlot.Id, excludeReservationId: reservation.Id, ct))
            {
                throw new ConflictException("This slot was just taken. Please pick another time.");
            }

            throw;
        }

        // Moving the row off the old slot frees it automatically (the filtered-unique guard only counts active rows).
        await PublishAsync(
            ReservationRoutingKeys.Rescheduled, reservation, newSlot.StartUtc, newSlot.EndUtc, reason, ct);
        _logger.LogInformation("Rescheduled reservation {ReservationId} to slot {SlotId}.", id, newSlot.Id);

        return await ProjectDetailAsync(id, ct);
    }

    public async Task<ReservationDetailDto> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var ownerId = await _db.Reservations.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => (Guid?)r.UserId)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException($"Reservation {id} was not found.");

        if (ownerId != CurrentUserId() && !IsStaffOrAdmin())
        {
            throw new ForbiddenException("You can only view your own reservations.");
        }

        return await ProjectDetailAsync(id, ct);
    }

    public async Task<PagedResult<ReservationDto>> ListMineAsync(
        ReservationListQuery filter, PaginationQuery pagination, CancellationToken ct = default)
    {
        // The owner is forced to the caller — a client filter can never widen it to someone else's bookings.
        var mine = filter with { UserId = CurrentUserId() };
        return await ListCoreAsync(mine, pagination, ct);
    }

    public Task<PagedResult<ReservationDto>> ListAsync(
        ReservationListQuery filter, PaginationQuery pagination, CancellationToken ct = default) =>
        ListCoreAsync(filter, pagination, ct);

    private async Task<PagedResult<ReservationDto>> ListCoreAsync(
        ReservationListQuery filter, PaginationQuery pagination, CancellationToken ct)
    {
        var query = _db.Reservations.AsNoTracking();

        if (filter.Status.HasValue)
        {
            query = query.Where(r => r.Status == filter.Status.Value);
        }

        if (filter.CourtId.HasValue)
        {
            query = query.Where(r => r.CourtId == filter.CourtId.Value);
        }

        if (filter.UserId.HasValue)
        {
            query = query.Where(r => r.UserId == filter.UserId.Value);
        }

        if (filter.FromUtc.HasValue)
        {
            query = query.Where(r => r.TimeSlot.StartUtc >= filter.FromUtc.Value);
        }

        if (filter.ToUtc.HasValue)
        {
            query = query.Where(r => r.TimeSlot.StartUtc < filter.ToUtc.Value);
        }

        // Newest-first (rubric §6: latest record on top); the projection preserves the order.
        var paged = await Project(query.OrderByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.Id))
            .ToPagedResultAsync(pagination, ct);

        return new PagedResult<ReservationDto>(
            paged.Items.Select(ToListDto).ToList(), paged.Page, paged.PageSize, paged.TotalCount);
    }

    // --- helpers ------------------------------------------------------------------------------------

    private Guid CurrentUserId() =>
        _currentUser.UserId ?? throw new UnauthorizedException("You must be signed in to manage reservations.");

    private bool IsStaffOrAdmin() =>
        _currentUser.IsInRole(Roles.Admin) || _currentUser.IsInRole(Roles.Staff);

    /// <summary>Guards a status-changing operation reserved for staff/admin, throwing 403 when the current user lacks
    /// the role. Centralizes the check so the service — not just the controller — enforces who may confirm/complete.</summary>
    private void EnsureStaffOrAdmin()
    {
        if (!IsStaffOrAdmin())
        {
            throw new ForbiddenException("Only staff or administrators can perform this operation.");
        }
    }

    private ReservationAudit NewAudit(ReservationStatus? oldStatus, ReservationStatus newStatus, string? reason, DateTime at) =>
        new()
        {
            OldStatus = oldStatus,
            NewStatus = newStatus,
            Reason = reason,
            ChangedByUserId = _currentUser.UserId,
            CreatedAtUtc = at,
        };

    private Task<bool> ActiveReservationExistsAsync(long slotId, long excludeReservationId, CancellationToken ct) =>
        _db.Reservations.AsNoTracking().AnyAsync(
            r => r.TimeSlotId == slotId
                 && r.Id != excludeReservationId
                 && (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed),
            ct);

    /// <summary>Loads the tracked reservation with its slot (for the event payload + the slot-ended completion check);
    /// a missing reservation is a clean 404.</summary>
    private async Task<Reservation> LoadWithSlotAsync(long id, CancellationToken ct) =>
        await _db.Reservations.Include(r => r.TimeSlot).FirstOrDefaultAsync(r => r.Id == id, ct)
        ?? throw new NotFoundException($"Reservation {id} was not found.");

    private Task PublishAsync(
        string routingKey, Reservation reservation, DateTime slotStartUtc, DateTime slotEndUtc, string? reason,
        CancellationToken ct) =>
        _events.PublishAsync(
            new ReservationEvent(
                routingKey, reservation.Id, reservation.UserId, reservation.CourtId, reservation.TimeSlotId,
                reservation.Status, reservation.TotalPrice, slotStartUtc, slotEndUtc, reason, _clock.UtcNow),
            ct);

    /// <summary>Re-reads one reservation (with audit trail + payment) into the detail DTO so writes return the same
    /// shape as the read path. Two bounded queries (reservation + ordered audits), names JOINed (no N+1).</summary>
    private async Task<ReservationDetailDto> ProjectDetailAsync(long id, CancellationToken ct)
    {
        var row = await Project(_db.Reservations.AsNoTracking().Where(r => r.Id == id)).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException($"Reservation {id} was not found.");

        var audits = await _db.ReservationAudits.AsNoTracking()
            .Where(a => a.ReservationId == id)
            .OrderBy(a => a.Id)
            .Select(a => new AuditRow(
                a.Id,
                a.OldStatus,
                a.NewStatus,
                a.Reason,
                a.ChangedBy != null ? a.ChangedBy.FirstName + " " + a.ChangedBy.LastName : null,
                a.ChangedBy != null ? a.ChangedBy.Email : null,
                a.CreatedAtUtc))
            .ToListAsync(ct);

        return new ReservationDetailDto(
            ToListDto(row),
            audits.Select(ToAuditDto).ToList(),
            ToPaymentDto(row));
    }

    /// <summary>Projection shared by the lists and the detail re-read: JOINs court + user (and payment scalars) so the
    /// DTO is built without an extra query. Labels are resolved in memory by the <c>ToXxxDto</c> mappers so the SQL
    /// stays provider-agnostic (Npgsql in prod, EF InMemory in tests).</summary>
    private static IQueryable<ReservationRow> Project(IQueryable<Reservation> source) =>
        source.Select(r => new ReservationRow(
            r.Id,
            r.UserId,
            r.User.FirstName + " " + r.User.LastName,
            r.User.Email,
            r.CourtId,
            r.Court.Name,
            r.TimeSlotId,
            r.TimeSlot.StartUtc,
            r.TimeSlot.EndUtc,
            r.TimeSlot.Bucket,
            r.Status,
            r.TotalPrice,
            r.CreatedAtUtc,
            r.CancelledAtUtc,
            r.CancellationReason,
            r.HoldExpiresAtUtc,
            r.Payment != null ? r.Payment.Id : null,
            r.Payment != null ? r.Payment.Status : null,
            r.Payment != null ? r.Payment.Amount : null,
            r.Payment != null ? r.Payment.AmountChargedCents : null,
            r.Payment != null ? r.Payment.CreatedAtUtc : null,
            r.Payment != null ? r.Payment.PaidAtUtc : null));

    private static ReservationDto ToListDto(ReservationRow r) =>
        new(
            r.Id,
            r.UserId,
            FullName(r.UserName, r.UserEmail),
            r.UserEmail,
            r.CourtId,
            r.CourtName,
            r.TimeSlotId,
            r.SlotStartUtc,
            r.SlotEndUtc,
            r.Bucket,
            BucketLabel(r.Bucket),
            r.Status,
            StatusLabel(r.Status),
            r.TotalPrice,
            r.PayStatus == PaymentStatus.Succeeded,
            r.CreatedAtUtc,
            r.CancelledAtUtc,
            r.CancellationReason,
            r.HoldExpiresAtUtc);

    private static ReservationAuditDto ToAuditDto(AuditRow a) =>
        new(
            a.Id,
            a.OldStatus,
            a.OldStatus.HasValue ? StatusLabel(a.OldStatus.Value) : null,
            a.NewStatus,
            StatusLabel(a.NewStatus),
            a.Reason,
            FullNameOrNull(a.ChangedByName, a.ChangedByEmail),
            a.CreatedAtUtc);

    private static ReservationPaymentDto? ToPaymentDto(ReservationRow r) =>
        r.PaymentId is null || r.PayStatus is null
            ? null
            : new ReservationPaymentDto(
                r.PaymentId.Value,
                r.PayStatus.Value,
                PaymentLabel(r.PayStatus.Value),
                r.PaymentAmount ?? 0m,
                r.PaymentAmountChargedCents,
                r.PayStatus.Value == PaymentStatus.Succeeded,
                r.PaymentCreatedAtUtc ?? default,
                r.PaymentPaidAtUtc);

    private static string FullName(string? name, string? email) =>
        FullNameOrNull(name, email) ?? "Unknown";

    private static string? FullNameOrNull(string? name, string? email)
    {
        var trimmed = name?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            return trimmed;
        }

        return string.IsNullOrWhiteSpace(email) ? null : email;
    }

    /// <summary>Human status label sent on the wire (so the client never maps the raw enum).</summary>
    private static string StatusLabel(ReservationStatus status) => status switch
    {
        ReservationStatus.Pending => "Pending",
        ReservationStatus.Confirmed => "Confirmed",
        ReservationStatus.Completed => "Completed",
        ReservationStatus.Cancelled => "Cancelled",
        _ => status.ToString(),
    };

    private static string BucketLabel(TimeOfDayBucket bucket) => bucket switch
    {
        TimeOfDayBucket.Morning => "Morning",
        TimeOfDayBucket.Afternoon => "Afternoon",
        TimeOfDayBucket.Evening => "Evening",
        _ => bucket.ToString(),
    };

    private static string PaymentLabel(PaymentStatus status) => status switch
    {
        PaymentStatus.Pending => "Pending",
        PaymentStatus.Succeeded => "Succeeded",
        PaymentStatus.Failed => "Failed",
        PaymentStatus.Refunded => "Refunded",
        PaymentStatus.RequiresReview => "Requires review",
        _ => status.ToString(),
    };

    /// <summary>Provider-agnostic intermediate for the reservation projection; the payment fields are null when the
    /// reservation has no payment row. Labels/IsPaid are resolved in memory by the mappers.</summary>
    private sealed record ReservationRow(
        long Id,
        Guid UserId,
        string? UserName,
        string? UserEmail,
        long CourtId,
        string CourtName,
        long TimeSlotId,
        DateTime SlotStartUtc,
        DateTime SlotEndUtc,
        TimeOfDayBucket Bucket,
        ReservationStatus Status,
        decimal TotalPrice,
        DateTime CreatedAtUtc,
        DateTime? CancelledAtUtc,
        string? CancellationReason,
        DateTime? HoldExpiresAtUtc,
        long? PaymentId,
        PaymentStatus? PayStatus,
        decimal? PaymentAmount,
        long? PaymentAmountChargedCents,
        DateTime? PaymentCreatedAtUtc,
        DateTime? PaymentPaidAtUtc);

    /// <summary>Provider-agnostic intermediate for the audit projection (actor name/email JOINed).</summary>
    private sealed record AuditRow(
        long Id,
        ReservationStatus? OldStatus,
        ReservationStatus NewStatus,
        string? Reason,
        string? ChangedByName,
        string? ChangedByEmail,
        DateTime CreatedAtUtc);
}
