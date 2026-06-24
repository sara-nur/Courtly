using Courtly.Contracts.Reference;
using FluentValidation;

namespace Courtly.Application.Common.Validation.Reference;

/// <summary>
/// Server-side validation for <c>City</c> create/update (feature 9). Messages spell out the exact format and
/// limits so the Flutter client can show them below the offending field. Discovered via the existing assembly
/// scan (<c>AddValidatorsFromAssemblyContaining&lt;RegisterRequestValidator&gt;</c>) — no per-validator
/// registration. Name uniqueness and CountryId existence are service-level checks, not validated here.
/// </summary>
public sealed class CreateCityRequestValidator : AbstractValidator<CreateCityRequest>
{
    private const int MaxNameLength = 100;

    public CreateCityRequestValidator()
    {
        RuleFor(r => r.Name)
            .NotEmpty().WithMessage("City name is required.")
            .MaximumLength(MaxNameLength).WithMessage($"City name must be at most {MaxNameLength} characters.");

        RuleFor(r => r.CountryId)
            .GreaterThan(0).WithMessage("A country must be selected.");
    }
}

public sealed class UpdateCityRequestValidator : AbstractValidator<UpdateCityRequest>
{
    private const int MaxNameLength = 100;

    public UpdateCityRequestValidator()
    {
        RuleFor(r => r.Name)
            .NotEmpty().WithMessage("City name is required.")
            .MaximumLength(MaxNameLength).WithMessage($"City name must be at most {MaxNameLength} characters.");

        RuleFor(r => r.CountryId)
            .GreaterThan(0).WithMessage("A country must be selected.");
    }
}
