using Courtly.Application.Common.Validation.Reservations;
using Courtly.Contracts.Reservations;
using Xunit;

namespace Courtly.Tests.Reservations;

/// <summary>
/// Feature 14: server-side validation for the reservation requests. The create request is just a slot id (the real
/// preconditions are business rules in the service); the cancel request requires a non-empty, bounded reason
/// (rubric §4 — clear, format-specific messages; §7 — a cancellation must carry a reason).
/// </summary>
public class ReservationValidatorTests
{
    [Fact]
    public void Create_requires_a_positive_slot_id()
    {
        var result = new CreateReservationRequestValidator().Validate(new CreateReservationRequest(0));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateReservationRequest.TimeSlotId));
    }

    [Fact]
    public void Create_accepts_a_real_slot_id()
    {
        var result = new CreateReservationRequestValidator().Validate(new CreateReservationRequest(42));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Cancel_requires_a_reason()
    {
        var result = new CancelReservationRequestValidator().Validate(new CancelReservationRequest(string.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CancelReservationRequest.Reason));
    }

    [Fact]
    public void Cancel_rejects_an_overlong_reason()
    {
        var result = new CancelReservationRequestValidator()
            .Validate(new CancelReservationRequest(new string('x', 501)));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Cancel_accepts_a_normal_reason()
    {
        var result = new CancelReservationRequestValidator().Validate(new CancelReservationRequest("Changed plans"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void AdminCreate_requires_a_slot_and_a_customer()
    {
        var result = new AdminCreateReservationRequestValidator()
            .Validate(new AdminCreateReservationRequest(0, Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AdminCreateReservationRequest.TimeSlotId));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AdminCreateReservationRequest.UserId));
    }

    [Fact]
    public void AdminCreate_accepts_a_slot_and_a_customer()
    {
        var result = new AdminCreateReservationRequestValidator()
            .Validate(new AdminCreateReservationRequest(42, Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Reschedule_requires_a_positive_new_slot_id()
    {
        var result = new RescheduleReservationRequestValidator().Validate(new RescheduleReservationRequest(0));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RescheduleReservationRequest.NewTimeSlotId));
    }

    [Fact]
    public void Reschedule_accepts_a_real_new_slot_id()
    {
        var result = new RescheduleReservationRequestValidator().Validate(new RescheduleReservationRequest(7));

        Assert.True(result.IsValid);
    }
}
