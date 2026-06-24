using Courtly.Contracts.Reference;
using FluentValidation;

namespace Courtly.Application.Common.Validation.Reference;

/// <summary>
/// Server-side validation for <c>Amenity</c> create/update (feature 9). Messages spell out the exact limits so
/// the Flutter client can render them below the offending field. Discovered via the existing assembly scan
/// (<c>AddValidatorsFromAssemblyContaining&lt;RegisterRequestValidator&gt;</c>) — no per-validator registration.
/// Name uniqueness is a service-level check, not validated here.
/// </summary>
public sealed class CreateAmenityRequestValidator : AbstractValidator<CreateAmenityRequest>
{
    private const int MaxNameLength = 50;
    private const int MaxIconKeyLength = 100;

    public CreateAmenityRequestValidator()
    {
        RuleFor(r => r.Name)
            .NotEmpty().WithMessage("Amenity name is required.")
            .MaximumLength(MaxNameLength).WithMessage($"Amenity name must be at most {MaxNameLength} characters.");

        RuleFor(r => r.IconKey)
            .MaximumLength(MaxIconKeyLength).WithMessage($"Icon key must be at most {MaxIconKeyLength} characters.");
    }
}

public sealed class UpdateAmenityRequestValidator : AbstractValidator<UpdateAmenityRequest>
{
    private const int MaxNameLength = 50;
    private const int MaxIconKeyLength = 100;

    public UpdateAmenityRequestValidator()
    {
        RuleFor(r => r.Name)
            .NotEmpty().WithMessage("Amenity name is required.")
            .MaximumLength(MaxNameLength).WithMessage($"Amenity name must be at most {MaxNameLength} characters.");

        RuleFor(r => r.IconKey)
            .MaximumLength(MaxIconKeyLength).WithMessage($"Icon key must be at most {MaxIconKeyLength} characters.");
    }
}
