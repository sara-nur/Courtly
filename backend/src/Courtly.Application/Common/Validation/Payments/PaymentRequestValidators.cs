using Courtly.Contracts.Payments;
using FluentValidation;

namespace Courtly.Application.Common.Validation.Payments;

/// <summary>
/// Server-side validation for starting a payment (feature 16). The shape is tiny — the client sends only a reservation
/// id; the real preconditions (the reservation exists, is owned by the caller, is Pending and unpaid) are business
/// rules enforced in <c>PaymentService</c> against live data. Discovered via the existing assembly scan — no
/// per-validator registration.
/// </summary>
public sealed class CreatePaymentIntentRequestValidator : AbstractValidator<CreatePaymentIntentRequest>
{
    public CreatePaymentIntentRequestValidator()
    {
        RuleFor(r => r.ReservationId)
            .GreaterThan(0).WithMessage("Select a reservation to pay for.");
    }
}

/// <summary>
/// Server-side validation for refunding a paid reservation (feature 16). A reason is mandatory (it rides to the
/// cancellation audit + notification — rubric §7); the message spells out the limit so the admin client can render it
/// below the field. The amount is never accepted from the client — the server refunds the actually-charged amount.
/// </summary>
public sealed class RefundReservationRequestValidator : AbstractValidator<RefundReservationRequest>
{
    private const int MaxReasonLength = 500;

    public RefundReservationRequestValidator()
    {
        RuleFor(r => r.Reason)
            .NotEmpty().WithMessage("A refund reason is required.")
            .MaximumLength(MaxReasonLength).WithMessage($"The reason must be at most {MaxReasonLength} characters.");
    }
}
