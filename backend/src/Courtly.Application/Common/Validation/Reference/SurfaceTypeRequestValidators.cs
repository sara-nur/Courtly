using Courtly.Contracts.Reference;
using FluentValidation;

namespace Courtly.Application.Common.Validation.Reference;

/// <summary>
/// Server-side validation for <c>SurfaceType</c> create/update (feature 9). Messages spell out the exact limits
/// so the Flutter client can render them below the offending field. Discovered via the existing assembly scan
/// (<c>AddValidatorsFromAssemblyContaining&lt;RegisterRequestValidator&gt;</c>) — no per-validator registration.
/// Name uniqueness is a service-level check, not validated here.
/// </summary>
public sealed class CreateSurfaceTypeRequestValidator : AbstractValidator<CreateSurfaceTypeRequest>
{
    private const int MaxNameLength = 50;
    private const int MaxDescriptionLength = 500;

    public CreateSurfaceTypeRequestValidator()
    {
        RuleFor(r => r.Name)
            .NotEmpty().WithMessage("Surface type name is required.")
            .MaximumLength(MaxNameLength).WithMessage($"Surface type name must be at most {MaxNameLength} characters.");

        RuleFor(r => r.Description)
            .MaximumLength(MaxDescriptionLength).WithMessage($"Description must be at most {MaxDescriptionLength} characters.");
    }
}

public sealed class UpdateSurfaceTypeRequestValidator : AbstractValidator<UpdateSurfaceTypeRequest>
{
    private const int MaxNameLength = 50;
    private const int MaxDescriptionLength = 500;

    public UpdateSurfaceTypeRequestValidator()
    {
        RuleFor(r => r.Name)
            .NotEmpty().WithMessage("Surface type name is required.")
            .MaximumLength(MaxNameLength).WithMessage($"Surface type name must be at most {MaxNameLength} characters.");

        RuleFor(r => r.Description)
            .MaximumLength(MaxDescriptionLength).WithMessage($"Description must be at most {MaxDescriptionLength} characters.");
    }
}
