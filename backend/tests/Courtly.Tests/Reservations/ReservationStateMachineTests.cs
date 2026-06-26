using Courtly.Application.Common.Exceptions;
using Courtly.Application.Reservations;
using Courtly.Domain.Enums;
using Xunit;

namespace Courtly.Tests.Reservations;

/// <summary>
/// Feature 14: the centralized reservation state machine. Lifecycle is <c>Pending → Confirmed → Completed</c> with
/// <c>Cancelled</c> reachable from either non-terminal state; <c>Completed</c>/<c>Cancelled</c> are terminal. These
/// assert the full transition table and that illegal/terminal transitions throw a <see cref="BusinessException"/>
/// (mapped to 409) — the rubric §7 "don't reject an already-approved request / don't act on a terminal record" rule.
/// </summary>
public class ReservationStateMachineTests
{
    [Theory]
    [InlineData(ReservationStatus.Pending, ReservationStatus.Confirmed)]
    [InlineData(ReservationStatus.Pending, ReservationStatus.Cancelled)]
    [InlineData(ReservationStatus.Confirmed, ReservationStatus.Completed)]
    [InlineData(ReservationStatus.Confirmed, ReservationStatus.Cancelled)]
    public void CanTransition_allows_the_legal_moves(ReservationStatus from, ReservationStatus to)
    {
        Assert.True(ReservationStateMachine.CanTransition(from, to));
        ReservationStateMachine.EnsureCanTransition(from, to); // does not throw
    }

    [Theory]
    [InlineData(ReservationStatus.Pending, ReservationStatus.Completed)]   // can't complete before it's confirmed
    [InlineData(ReservationStatus.Confirmed, ReservationStatus.Pending)]   // no going back
    [InlineData(ReservationStatus.Completed, ReservationStatus.Cancelled)] // terminal
    [InlineData(ReservationStatus.Completed, ReservationStatus.Confirmed)] // terminal
    [InlineData(ReservationStatus.Cancelled, ReservationStatus.Confirmed)] // terminal
    [InlineData(ReservationStatus.Cancelled, ReservationStatus.Completed)] // terminal
    public void EnsureCanTransition_rejects_illegal_moves(ReservationStatus from, ReservationStatus to)
    {
        Assert.False(ReservationStateMachine.CanTransition(from, to));
        Assert.Throws<BusinessException>(() => ReservationStateMachine.EnsureCanTransition(from, to));
    }

    [Theory]
    [InlineData(ReservationStatus.Completed, true)]
    [InlineData(ReservationStatus.Cancelled, true)]
    [InlineData(ReservationStatus.Pending, false)]
    [InlineData(ReservationStatus.Confirmed, false)]
    public void IsTerminal_marks_the_end_states(ReservationStatus status, bool terminal) =>
        Assert.Equal(terminal, ReservationStateMachine.IsTerminal(status));

    [Theory]
    [InlineData(ReservationStatus.Pending, true)]
    [InlineData(ReservationStatus.Confirmed, true)]
    [InlineData(ReservationStatus.Completed, false)]
    [InlineData(ReservationStatus.Cancelled, false)]
    public void IsActive_marks_the_slot_holding_states(ReservationStatus status, bool active) =>
        Assert.Equal(active, ReservationStateMachine.IsActive(status));

    [Fact]
    public void EnsureCanTransition_on_a_terminal_reservation_explains_it_is_already_closed()
    {
        var ex = Assert.Throws<BusinessException>(
            () => ReservationStateMachine.EnsureCanTransition(ReservationStatus.Cancelled, ReservationStatus.Confirmed));
        Assert.Contains("already", ex.Message);
    }
}
