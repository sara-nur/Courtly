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
