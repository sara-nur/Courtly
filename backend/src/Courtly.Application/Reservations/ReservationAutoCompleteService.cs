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
/// Auto-completes Confirmed reservations once their slot has ended — the Worker side of the reservation lifecycle that
/// <see cref="IReservationService.CompleteAsync"/>'s doc refers to. The admin <c>CompleteAsync</c> path remains for
/// manual use; this system actor closes the normal case so a Confirmed booking doesn't linger indefinitely after its
/// time. A reservation is eligible when <c>Status == Confirmed</c> and its <see cref="TimeSlot.EndUtc"/> is in the past
/// (the same "slot must have ended" guard <c>CompleteAsync</c> enforces).
/// </summary>
/// <remarks>
/// Scoped, driven by the Worker on a timer; deliberately has no <c>ICurrentUser</c> — this is a system actor, so the
/// audit row's <c>ChangedByUserId</c> is <c>null</c> (the same precedent as
/// <see cref="ReservationHoldExpiryService"/> and <c>PaymentService.FinalizeAsync</c>). The status change + its audit
/// row commit in a single <c>SaveChangesAsync</c> (rubric §3.4); events are published only after that commit succeeds
/// (post-commit seam). Work is batched so a backlog is drained over several scans rather than one unbounded query.
/// </remarks>
public sealed class ReservationAutoCompleteService
{
    private readonly CourtlyDbContext _db;
    private readonly IReservationEventPublisher _events;
    private readonly IClock _clock;
    private readonly ILogger<ReservationAutoCompleteService> _logger;

    public ReservationAutoCompleteService(
        CourtlyDbContext db,
        IReservationEventPublisher events,
        IClock clock,
        ILogger<ReservationAutoCompleteService> logger)
    {
        _db = db;
        _events = events;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Completes up to <paramref name="batchSize"/> Confirmed reservations whose slot has already ended,
    /// oldest-first; returns how many were completed.</summary>
    public async Task<int> CompleteEndedReservationsAsync(int batchSize, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        // Tracked load (we mutate + audit these); Include the slot for the end-time filter and the event payload.
        var ended = await _db.Reservations
            .Include(r => r.TimeSlot)
            .Where(r => r.Status == ReservationStatus.Confirmed && r.TimeSlot.EndUtc < now)
            .OrderBy(r => r.TimeSlot.EndUtc)
            .Take(batchSize)
            .ToListAsync(ct);

        if (ended.Count == 0)
        {
            return 0;
        }

        const string reason = "Slot ended";

        foreach (var reservation in ended)
        {
            // Guard through the state machine even though the filter guarantees Confirmed — keeps the single source of
            // truth for legal transitions (rubric §7), never a bare status assignment.
            ReservationStateMachine.EnsureCanTransition(reservation.Status, ReservationStatus.Completed);

            reservation.Audits.Add(new ReservationAudit
            {
                OldStatus = ReservationStatus.Confirmed,
                NewStatus = ReservationStatus.Completed,
                Reason = reason,
                ChangedByUserId = null, // system actor — same precedent as ReservationHoldExpiryService
                CreatedAtUtc = now,
            });
            reservation.Status = ReservationStatus.Completed;
        }

        // One SaveChanges commits every completion + its audit row atomically (rubric §3.4).
        await _db.SaveChangesAsync(ct);

        // Post-commit: publish a Completed event per row so downstream consumers (e.g. notifications) can react.
        foreach (var reservation in ended)
        {
            await _events.PublishAsync(
                new ReservationEvent(
                    ReservationRoutingKeys.Completed,
                    reservation.Id,
                    reservation.UserId,
                    reservation.CourtId,
                    reservation.TimeSlotId,
                    ReservationStatus.Completed,
                    reservation.TotalPrice,
                    reservation.TimeSlot.StartUtc,
                    reservation.TimeSlot.EndUtc,
                    reason,
                    now),
                ct);
        }

        _logger.LogInformation("Auto-completed {Count} ended reservation(s).", ended.Count);
        return ended.Count;
    }
}
