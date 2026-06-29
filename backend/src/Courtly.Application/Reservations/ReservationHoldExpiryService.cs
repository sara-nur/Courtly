using Courtly.Application.Abstractions;
using Courtly.Contracts.Messaging;
using Courtly.Domain.Entities;
using Courtly.Domain.Enums;
using Courtly.Infrastructure.Messaging;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Courtly.Application.Reservations;

/// <summary>
/// Auto-cancels expired unpaid holds (roadmap amendment to feature 14). A booking is created <c>Pending</c> with a
/// <see cref="Reservation.HoldExpiresAtUtc"/> deadline; payment success flips it <c>Pending → Confirmed</c>. So
/// <c>Status == Pending</c> ⇔ still unpaid — no <see cref="Payment"/> inspection is needed to decide the cancel.
/// Cancelling frees the slot automatically: the filtered-unique overlap index
/// (<c>ux_reservations_active_timeslot</c>) counts only active (Pending|Confirmed) rows, so a Cancelled row no longer
/// holds the slot (see <see cref="ReservationStateMachine.IsActive"/>).
/// </summary>
/// <remarks>
/// Scoped, driven by the Worker on a timer (feature 17); deliberately has no <c>ICurrentUser</c> — this is a system
/// actor, so the audit row's <c>ChangedByUserId</c> is <c>null</c> (the same precedent as
/// <c>PaymentService.FinalizeAsync</c>). The status change + its audit row commit in a single
/// <c>SaveChangesAsync</c> (rubric §3.4); events are published only after that commit succeeds (post-commit seam), so a
/// publish failure can never roll back a persisted cancellation. Work is batched so a backlog is drained over several
/// scans rather than one unbounded query.
/// </remarks>
public sealed class ReservationHoldExpiryService
{
    private readonly CourtlyDbContext _db;
    private readonly IReservationEventPublisher _events;
    private readonly IClock _clock;
    private readonly ILogger<ReservationHoldExpiryService> _logger;

    public ReservationHoldExpiryService(
        CourtlyDbContext db,
        IReservationEventPublisher events,
        IClock clock,
        ILogger<ReservationHoldExpiryService> logger)
    {
        _db = db;
        _events = events;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Cancels up to <paramref name="batchSize"/> Pending reservations whose hold deadline has passed,
    /// oldest-first; returns how many were cancelled.</summary>
    public async Task<int> CancelExpiredHoldsAsync(int batchSize, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        // Tracked load (we mutate + audit these); Include the slot for the event payload's window.
        var expired = await _db.Reservations
            .Include(r => r.TimeSlot)
            .Where(r => r.Status == ReservationStatus.Pending
                        && r.HoldExpiresAtUtc != null
                        && r.HoldExpiresAtUtc < now)
            .OrderBy(r => r.HoldExpiresAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);

        if (expired.Count == 0)
        {
            return 0;
        }

        const string reason = "Reservation hold expired";

        foreach (var reservation in expired)
        {
            // Guard through the state machine even though the filter guarantees Pending — keeps the single source of
            // truth for legal transitions (rubric §7), never a bare status assignment.
            ReservationStateMachine.EnsureCanTransition(reservation.Status, ReservationStatus.Cancelled);

            reservation.Status = ReservationStatus.Cancelled;
            reservation.CancelledAtUtc = now;
            reservation.CancellationReason = reason;
            reservation.Audits.Add(new ReservationAudit
            {
                OldStatus = ReservationStatus.Pending,
                NewStatus = ReservationStatus.Cancelled,
                Reason = reason,
                ChangedByUserId = null, // system actor — same precedent as PaymentService.FinalizeAsync
                CreatedAtUtc = now,
            });
        }

        // One SaveChanges commits every cancellation + its audit row atomically (rubric §3.4).
        await _db.SaveChangesAsync(ct);

        // Post-commit: publish a Cancelled event per row so the Worker can email the user (feature 17).
        foreach (var reservation in expired)
        {
            await _events.PublishAsync(
                new ReservationEvent(
                    ReservationRoutingKeys.Cancelled,
                    reservation.Id,
                    reservation.UserId,
                    reservation.CourtId,
                    reservation.TimeSlotId,
                    ReservationStatus.Cancelled,
                    reservation.TotalPrice,
                    reservation.TimeSlot.StartUtc,
                    reservation.TimeSlot.EndUtc,
                    reason,
                    now),
                ct);
        }

        _logger.LogInformation("Auto-cancelled {Count} expired reservation hold(s).", expired.Count);
        return expired.Count;
    }
}
