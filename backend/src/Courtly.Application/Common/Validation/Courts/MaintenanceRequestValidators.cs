using Courtly.Contracts.Court;
using FluentValidation;

namespace Courtly.Application.Common.Validation.Courts;

/// <summary>
/// Server-side validation for opening a maintenance window (feature 12). Messages spell out the exact limits so the
/// Flutter client can render them below the offending field. Discovered via the existing assembly scan — no
/// per-validator registration. The timing semantics (a null/past start ⇒ "now"; a future start ⇒ scheduled) and the
/// overlap rule are enforced in <c>MaintenanceService</c>; this only guards the user-supplied shape: a non-empty
/// reason and, when both ends are given, an end strictly after the start.
/// </summary>
public sealed class CreateMaintenanceRequestValidator : AbstractValidator<CreateMaintenanceRequest>
{
    private const int MaxReasonLength = 500;

    public CreateMaintenanceRequestValidator()
    {
        RuleFor(r => r.Reason)
            .NotEmpty().WithMessage("A maintenance reason is required.")
            .MaximumLength(MaxReasonLength)
            .WithMessage($"The reason must be at most {MaxReasonLength} characters.");

        // Only validatable here when BOTH are supplied; the "end after now" case (start omitted) is checked in the
        // service against the server clock.
        RuleFor(r => r.EndUtc)
            .Must((model, end) => !end.HasValue || !model.StartUtc.HasValue || end.Value > model.StartUtc.Value)
            .WithMessage("The maintenance end must be after its start.");
    }
}
