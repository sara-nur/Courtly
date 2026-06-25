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
        this.ApplyLocationRules(r => r.Latitude, r => r.Longitude);
    }
}

public sealed class UpdateCourtRequestValidator : AbstractValidator<UpdateCourtRequest>
{
    public UpdateCourtRequestValidator()
    {
        this.ApplyCourtRules(
            r => r.Name, r => r.Description, r => r.CityId, r => r.SurfaceTypeId, r => r.CourtTypeId, r => r.HourlyPrice);
        this.ApplyLocationRules(r => r.Latitude, r => r.Longitude);
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

    /// <summary>The optional map location rules shared by Create/Update: latitude and longitude are both-or-neither
    /// (the map picker either sets both or clears both), latitude is in [-90, 90], and longitude is in [-180, 180].
    /// Messages are user-facing so the Flutter client can render them under the map field.</summary>
    public static void ApplyLocationRules<T>(
        this AbstractValidator<T> validator,
        System.Linq.Expressions.Expression<Func<T, double?>> latitude,
        System.Linq.Expressions.Expression<Func<T, double?>> longitude)
    {
        var latGetter = latitude.Compile();
        var lngGetter = longitude.Compile();

        // Both-or-neither: if exactly one coordinate is set, the location is incomplete — reject it on both fields.
        validator.RuleFor(latitude)
            .Must((model, _) => latGetter(model).HasValue == lngGetter(model).HasValue)
            .WithMessage("Latitude and longitude must be set together.");

        validator.RuleFor(latitude)
            .Must(lat => !lat.HasValue || (lat.Value >= -90d && lat.Value <= 90d))
            .WithMessage("Latitude must be between -90 and 90.");

        validator.RuleFor(longitude)
            .Must(lng => !lng.HasValue || (lng.Value >= -180d && lng.Value <= 180d))
            .WithMessage("Longitude must be between -180 and 180.");
    }
}
