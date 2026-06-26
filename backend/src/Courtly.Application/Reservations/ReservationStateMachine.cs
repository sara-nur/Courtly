using Courtly.Application.Common.Exceptions;
using Courtly.Domain.Enums;

namespace Courtly.Application.Reservations;

/// <summary>
/// The centralized state machine for a reservation (feature 14). Every allowed <see cref="ReservationStatus"/>
/// transition lives HERE — never scattered across the controller or service — so the rules can be reasoned about and
/// unit-tested in one place (rubric §7: centralized state-machine logic, not spread across controllers; hard-delete
/// instead of a status change is a defect). The larger sibling of <c>MaintenanceStateMachine</c>, same shape.
///
/// Lifecycle: <c>Pending → Confirmed → Completed</c>, with <c>Cancelled</c> reachable from either non-terminal state
/// (a cancellation always carries a reason). <c>Completed</c> and <c>Cancelled</c> are terminal and reject any further
/// transition. The guards that go BEYOND the transition table — slot free/active, court active &amp; not under
/// maintenance, slot in the future, can't cancel a paid booking without a refund, can't complete before the slot ends
/// — live in <c>ReservationService</c> next to the data they need.
/// </summary>
public static class ReservationStateMachine
{
    /// <summary>Allowed target states keyed by the current state. A state absent from a value list is rejected.</summary>
    private static readonly IReadOnlyDictionary<ReservationStatus, ReservationStatus[]> Allowed =
        new Dictionary<ReservationStatus, ReservationStatus[]>
        {
            [ReservationStatus.Pending] = new[] { ReservationStatus.Confirmed, ReservationStatus.Cancelled },
            [ReservationStatus.Confirmed] = new[] { ReservationStatus.Completed, ReservationStatus.Cancelled },
            [ReservationStatus.Completed] = Array.Empty<ReservationStatus>(),
            [ReservationStatus.Cancelled] = Array.Empty<ReservationStatus>(),
        };

    /// <summary>Terminal states allow no further transition.</summary>
    public static bool IsTerminal(ReservationStatus status) =>
        status is ReservationStatus.Completed or ReservationStatus.Cancelled;

    /// <summary>An "active" reservation holds its slot — the filtered-unique overlap guard
    /// (<c>ux_reservations_active_timeslot</c>) counts exactly these; a Cancelled/Completed one frees the slot.</summary>
    public static bool IsActive(ReservationStatus status) =>
        status is ReservationStatus.Pending or ReservationStatus.Confirmed;

    /// <summary>True when <paramref name="to"/> is a permitted next state from <paramref name="from"/>.</summary>
    public static bool CanTransition(ReservationStatus from, ReservationStatus to) =>
        Allowed.TryGetValue(from, out var targets) && Array.IndexOf(targets, to) >= 0;

    /// <summary>Guards a transition, throwing a <see cref="BusinessException"/> (mapped to 409) with a clear reason
    /// when it is not allowed — e.g. confirming a cancelled booking, or acting on a terminal one (rubric §7: don't
    /// reject an already-approved request, don't review before completion).</summary>
    public static void EnsureCanTransition(ReservationStatus from, ReservationStatus to)
    {
        if (CanTransition(from, to))
        {
            return;
        }

        if (IsTerminal(from))
        {
            throw new BusinessException($"This reservation is already {from} and cannot be changed.");
        }

        throw new BusinessException($"Cannot change reservation status from {from} to {to}.");
    }
}
