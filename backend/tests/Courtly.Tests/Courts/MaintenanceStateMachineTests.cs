using Courtly.Application.Common.Exceptions;
using Courtly.Application.Courts.Maintenance;
using Courtly.Domain.Enums;
using Xunit;

namespace Courtly.Tests.Courts;

/// <summary>
/// Feature 12: the centralized maintenance state machine. Lifecycle is
/// <c>Scheduled → InProgress → Completed</c> (the "Fix" path) with <c>Cancelled</c> reachable from either
/// non-terminal state; <c>Completed</c>/<c>Cancelled</c> are terminal. These assert the full transition table and
/// that illegal/terminal transitions throw a <see cref="BusinessException"/> (mapped to 409).
/// </summary>
public class MaintenanceStateMachineTests
{
    [Theory]
    [InlineData(MaintenanceStatus.Scheduled, MaintenanceStatus.InProgress)]
    [InlineData(MaintenanceStatus.Scheduled, MaintenanceStatus.Cancelled)]
    [InlineData(MaintenanceStatus.InProgress, MaintenanceStatus.Completed)]
    [InlineData(MaintenanceStatus.InProgress, MaintenanceStatus.Cancelled)]
    public void CanTransition_allows_the_legal_moves(MaintenanceStatus from, MaintenanceStatus to)
    {
        Assert.True(MaintenanceStateMachine.CanTransition(from, to));
        MaintenanceStateMachine.EnsureCanTransition(from, to); // does not throw
    }

    [Theory]
    [InlineData(MaintenanceStatus.Scheduled, MaintenanceStatus.Completed)] // must be started before it can be fixed
    [InlineData(MaintenanceStatus.InProgress, MaintenanceStatus.Scheduled)] // cannot go back to scheduled
    [InlineData(MaintenanceStatus.Completed, MaintenanceStatus.InProgress)] // terminal
    [InlineData(MaintenanceStatus.Completed, MaintenanceStatus.Cancelled)] // terminal
    [InlineData(MaintenanceStatus.Cancelled, MaintenanceStatus.InProgress)] // terminal
    public void EnsureCanTransition_rejects_illegal_moves(MaintenanceStatus from, MaintenanceStatus to)
    {
        Assert.False(MaintenanceStateMachine.CanTransition(from, to));
        Assert.Throws<BusinessException>(() => MaintenanceStateMachine.EnsureCanTransition(from, to));
    }

    [Theory]
    [InlineData(MaintenanceStatus.Completed, true)]
    [InlineData(MaintenanceStatus.Cancelled, true)]
    [InlineData(MaintenanceStatus.Scheduled, false)]
    [InlineData(MaintenanceStatus.InProgress, false)]
    public void IsTerminal_and_IsOpen_are_complementary(MaintenanceStatus status, bool terminal)
    {
        Assert.Equal(terminal, MaintenanceStateMachine.IsTerminal(status));
        Assert.Equal(!terminal, MaintenanceStateMachine.IsOpen(status));
    }

    [Fact]
    public void EnsureCanTransition_on_a_terminal_window_explains_it_is_already_closed()
    {
        var ex = Assert.Throws<BusinessException>(
            () => MaintenanceStateMachine.EnsureCanTransition(MaintenanceStatus.Completed, MaintenanceStatus.Cancelled));
        Assert.Contains("already", ex.Message);
    }
}
