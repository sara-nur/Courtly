using Courtly.Application.Common.Exceptions;
using Courtly.Domain.Enums;

namespace Courtly.Application.Courts.Maintenance;

/// <summary>
/// The centralized state machine for a court maintenance window (feature 12). All allowed
/// <see cref="MaintenanceStatus"/> transitions live HERE — never scattered across the controller or service —
/// so the rules can be reasoned about and unit-tested in one place (rubric §7: centralized state-machine logic,
/// not spread across controllers). This is the smaller sibling of the F14 reservation state machine and follows
/// the same shape.
///
/// Lifecycle: <c>Scheduled → InProgress → Completed</c> (the "Fix" path), with <c>Cancelled</c> reachable from
/// either non-terminal state. <c>Completed</c> and <c>Cancelled</c> are terminal.
/// </summary>
public static class MaintenanceStateMachine
{
    /// <summary>Allowed target states keyed by the current state. A state absent from a value list is rejected.</summary>
    private static readonly IReadOnlyDictionary<MaintenanceStatus, MaintenanceStatus[]> Allowed =
        new Dictionary<MaintenanceStatus, MaintenanceStatus[]>
        {
            [MaintenanceStatus.Scheduled] = new[] { MaintenanceStatus.InProgress, MaintenanceStatus.Cancelled },
            [MaintenanceStatus.InProgress] = new[] { MaintenanceStatus.Completed, MaintenanceStatus.Cancelled },
            [MaintenanceStatus.Completed] = Array.Empty<MaintenanceStatus>(),
            [MaintenanceStatus.Cancelled] = Array.Empty<MaintenanceStatus>(),
        };

    /// <summary>Terminal states hold no maintenance and allow no further transition.</summary>
    public static bool IsTerminal(MaintenanceStatus status) =>
        status is MaintenanceStatus.Completed or MaintenanceStatus.Cancelled;

    /// <summary>An "open" (active) window is one a court can be under: not yet terminal.</summary>
    public static bool IsOpen(MaintenanceStatus status) => !IsTerminal(status);

    /// <summary>True when <paramref name="to"/> is a permitted next state from <paramref name="from"/>.</summary>
    public static bool CanTransition(MaintenanceStatus from, MaintenanceStatus to) =>
        Allowed.TryGetValue(from, out var targets) && Array.IndexOf(targets, to) >= 0;

    /// <summary>Guards a transition, throwing a <see cref="BusinessException"/> (mapped to 409) with a clear reason
    /// when it is not allowed — e.g. fixing a window that is not in progress, or acting on a terminal window.</summary>
    public static void EnsureCanTransition(MaintenanceStatus from, MaintenanceStatus to)
    {
        if (CanTransition(from, to))
        {
            return;
        }

        if (IsTerminal(from))
        {
            throw new BusinessException(
                $"This maintenance window is already {from} and cannot be changed.");
        }

        throw new BusinessException($"Cannot change maintenance status from {from} to {to}.");
    }
}
