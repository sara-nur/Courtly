using Courtly.Contracts.Court;
using FluentValidation;

namespace Courtly.Application.Common.Validation.Courts;

/// <summary>
/// Server-side validation for <c>Court</c> create/update (feature 10). Messages spell out the exact limits so the
/// Flutter client can render them below the offending field. Discovered via the existing assembly scan
/// (<c>AddValidatorsFromAssemblyContaining&lt;RegisterRequestValidator&gt;</c>) — no per-validator registration.
/// The City/SurfaceType/CourtType existence checks are service-level (clean 404), not validated here; this only
/// guards that an id was actually selected (> 0). Name uniqueness is not enforced (court names may repeat).
/// </summary>
public sealed class CreateCourtRequestValidator : AbstractValidator<CreateCourtRequest>
{
    public CreateCourtRequestValidator()
    {
        this.ApplyCourtRules(
            r => r.Name, r => r.Description, r => r.CityId, r => r.SurfaceTypeId, r => r.CourtTypeId, r => r.HourlyPrice);
    }
}

public sealed class UpdateCourtRequestValidator : AbstractValidator<UpdateCourtRequest>
{
    public UpdateCourtRequestValidator()
    {
        this.ApplyCourtRules(
            r => r.Name, r => r.Description, r => r.CityId, r => r.SurfaceTypeId, r => r.CourtTypeId, r => r.HourlyPrice);
    }
}

/// <summary>Shared rule set so Create/Update validate identically (same fields, same messages).</summary>
internal static class CourtValidationRules
{
    private const int MaxNameLength = 200;
    private const int MaxDescriptionLength = 2000;
    private const decimal MaxHourlyPrice = 100_000m;

    public static void ApplyCourtRules<T>(
        this AbstractValidator<T> validator,
        System.Linq.Expressions.Expression<Func<T, string>> name,
        System.Linq.Expressions.Expression<Func<T, string?>> description,
        System.Linq.Expressions.Expression<Func<T, long>> cityId,
        System.Linq.Expressions.Expression<Func<T, long>> surfaceTypeId,
        System.Linq.Expressions.Expression<Func<T, long>> courtTypeId,
        System.Linq.Expressions.Expression<Func<T, decimal>> hourlyPrice)
    {
        validator.RuleFor(name)
            .NotEmpty().WithMessage("Court name is required.")
            .MaximumLength(MaxNameLength).WithMessage($"Court name must be at most {MaxNameLength} characters.");

        validator.RuleFor(description)
            .MaximumLength(MaxDescriptionLength).WithMessage($"Description must be at most {MaxDescriptionLength} characters.");

        validator.RuleFor(cityId)
            .GreaterThan(0).WithMessage("Please select a city.");

        validator.RuleFor(surfaceTypeId)
            .GreaterThan(0).WithMessage("Please select a surface type.");

        validator.RuleFor(courtTypeId)
            .GreaterThan(0).WithMessage("Please select a court type.");

        validator.RuleFor(hourlyPrice)
            .GreaterThan(0).WithMessage("Hourly price must be greater than 0.")
            .LessThanOrEqualTo(MaxHourlyPrice).WithMessage($"Hourly price must be at most {MaxHourlyPrice:0}.");
    }
}
