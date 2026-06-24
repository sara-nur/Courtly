using Courtly.Application.Common.Validation.Courts;
using Courtly.Contracts.Court;
using Xunit;

namespace Courtly.Tests.Validation;

/// <summary>Feature 10 DoD (auto): court create input is validated server-side with explicit, limit-stating
/// messages the client renders below the field (rubric §4). FK existence is a service-level concern (clean 404);
/// the validator only guards that an id was selected and the price/length limits.</summary>
public class CourtRequestValidatorTests
{
    private readonly CreateCourtRequestValidator _validator = new();

    private static CreateCourtRequest Request(
        string name = "Center Court", string? description = "Main court.", long cityId = 1,
        long surfaceTypeId = 1, long courtTypeId = 1, decimal hourlyPrice = 20m) =>
        new(name, description, cityId, surfaceTypeId, courtTypeId, false, true, false, hourlyPrice);

    [Fact]
    public void Valid_request_passes()
    {
        Assert.True(_validator.Validate(Request()).IsValid);
    }

    [Fact]
    public void Empty_name_is_rejected_as_required()
    {
        var result = _validator.Validate(Request(name: "  "));

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors, e => e.PropertyName == nameof(CreateCourtRequest.Name));
        Assert.Contains("required", error.ErrorMessage);
    }

    [Fact]
    public void Overlong_name_is_rejected_with_the_limit()
    {
        var result = _validator.Validate(Request(name: new string('x', 201)));

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors, e => e.PropertyName == nameof(CreateCourtRequest.Name));
        Assert.Contains("200", error.ErrorMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Non_positive_price_is_rejected_with_a_format_message(decimal price)
    {
        var result = _validator.Validate(Request(hourlyPrice: price));

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors, e => e.PropertyName == nameof(CreateCourtRequest.HourlyPrice));
        Assert.Contains("greater than 0", error.ErrorMessage);
    }

    [Fact]
    public void Unselected_foreign_keys_are_rejected()
    {
        var result = _validator.Validate(Request(cityId: 0, surfaceTypeId: 0, courtTypeId: 0));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCourtRequest.CityId));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCourtRequest.SurfaceTypeId));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCourtRequest.CourtTypeId));
    }
}
