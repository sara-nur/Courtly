using Courtly.Contracts.Reference;
using FluentValidation;

namespace Courtly.Application.Common.Validation.Reference;

/// <summary>
/// Server-side validation for <c>Country</c> create/update (feature 9). Messages spell out the exact format
/// and limits so the Flutter client can show them below the offending field. Discovered via the existing
/// assembly scan (<c>AddValidatorsFromAssemblyContaining&lt;RegisterRequestValidator&gt;</c>) — no per-validator
/// registration. Uniqueness (Name/IsoCode) is a service-level check, not validated here.
/// </summary>
public sealed class CreateCountryRequestValidator : AbstractValidator<CreateCountryRequest>
{
    private const int MaxNameLength = 100;

    public CreateCountryRequestValidator()
    {
        RuleFor(r => r.Name)
            .NotEmpty().WithMessage("Country name is required.")
            .MaximumLength(MaxNameLength).WithMessage($"Country name must be at most {MaxNameLength} characters.");

        RuleFor(r => r.IsoCode)
            .NotEmpty().WithMessage("ISO code is required.")
            .Matches("^[A-Za-z]{3}$").WithMessage("ISO code must be exactly 3 letters, e.g. BIH.");
    }
}

public sealed class UpdateCountryRequestValidator : AbstractValidator<UpdateCountryRequest>
{
    private const int MaxNameLength = 100;

    public UpdateCountryRequestValidator()
    {
        RuleFor(r => r.Name)
            .NotEmpty().WithMessage("Country name is required.")
            .MaximumLength(MaxNameLength).WithMessage($"Country name must be at most {MaxNameLength} characters.");

        RuleFor(r => r.IsoCode)
            .NotEmpty().WithMessage("ISO code is required.")
            .Matches("^[A-Za-z]{3}$").WithMessage("ISO code must be exactly 3 letters, e.g. BIH.");
    }
}
