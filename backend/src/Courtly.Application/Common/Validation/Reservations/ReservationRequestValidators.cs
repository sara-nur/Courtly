using Courtly.Contracts.Reservations;
using FluentValidation;

namespace Courtly.Application.Common.Validation.Reservations;

/// <summary>
/// Server-side validation for booking a slot (feature 14). The shape is tiny — the client sends only a slot id; the
/// real preconditions (slot active/free, court active &amp; not under maintenance, slot in the future, no overlap) are
/// business rules enforced in <c>ReservationService</c> against live data. Discovered via the existing assembly scan —
/// no per-validator registration.
/// </summary>
public sealed class CreateReservationRequestValidator : AbstractValidator<CreateReservationRequest>
{
    public CreateReservationRequestValidator()
    {
        RuleFor(r => r.TimeSlotId)
            .GreaterThan(0).WithMessage("Select a time slot to book.");
    }
}

/// <summary>
/// Server-side validation for an admin manual booking (feature 15 "+ New Booking"): a slot must be chosen and a
/// customer selected. That the user exists and is active is a business rule checked against live data in
/// <c>ReservationService</c>.
/// </summary>
public sealed class AdminCreateReservationRequestValidator : AbstractValidator<AdminCreateReservationRequest>
{
    public AdminCreateReservationRequestValidator()
    {
        RuleFor(r => r.TimeSlotId)
            .GreaterThan(0).WithMessage("Select a time slot to book.");

        RuleFor(r => r.UserId)
            .NotEmpty().WithMessage("Select a customer for the booking.");
    }
}

/// <summary>
/// Server-side validation for cancelling a reservation (feature 14). A reason is mandatory (rubric §7: a cancellation
/// must carry a reason and raise a notification); the message spells out the limit so the Flutter client can render it
/// below the field.
/// </summary>
public sealed class CancelReservationRequestValidator : AbstractValidator<CancelReservationRequest>
{
    private const int MaxReasonLength = 500;

    public CancelReservationRequestValidator()
    {
        RuleFor(r => r.Reason)
            .NotEmpty().WithMessage("A cancellation reason is required.")
            .MaximumLength(MaxReasonLength).WithMessage($"The reason must be at most {MaxReasonLength} characters.");
    }
}

/// <summary>
/// Server-side validation for rescheduling a reservation (feature 15): a new slot must be chosen. Whether the slot is
/// free/active and the reservation is non-terminal and unpaid are business rules enforced in
/// <c>ReservationService</c> against live data.
/// </summary>
public sealed class RescheduleReservationRequestValidator : AbstractValidator<RescheduleReservationRequest>
{
    public RescheduleReservationRequestValidator()
    {
        RuleFor(r => r.NewTimeSlotId)
            .GreaterThan(0).WithMessage("Select a new time slot.");
    }
}
