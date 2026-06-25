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
        long surfaceTypeId = 1, long courtTypeId = 1, decimal hourlyPrice = 20m,
        double? latitude = null, double? longitude = null) =>
        new(name, description, cityId, surfaceTypeId, courtTypeId, false, true, false, hourlyPrice, latitude, longitude);

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

    // --- Map location (feature 11): both-or-neither + ranges --------------------------------------

    [Fact]
    public void No_location_is_valid()
    {
        Assert.True(_validator.Validate(Request(latitude: null, longitude: null)).IsValid);
    }

    [Fact]
    public void Valid_lat_lng_pair_passes()
    {
        Assert.True(_validator.Validate(Request(latitude: 43.8563, longitude: 18.4131)).IsValid);
    }

    [Fact]
    public void Latitude_without_longitude_is_rejected_as_incomplete()
    {
        var result = _validator.Validate(Request(latitude: 43.8563, longitude: null));

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors, e => e.PropertyName == nameof(CreateCourtRequest.Latitude));
        Assert.Contains("together", error.ErrorMessage);
    }

    [Fact]
    public void Longitude_without_latitude_is_rejected_as_incomplete()
    {
        var result = _validator.Validate(Request(latitude: null, longitude: 18.4131));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCourtRequest.Latitude));
    }

    [Theory]
    [InlineData(91)]
    [InlineData(-91)]
    public void Latitude_out_of_range_is_rejected(double latitude)
    {
        var result = _validator.Validate(Request(latitude: latitude, longitude: 18.4131));

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors, e => e.PropertyName == nameof(CreateCourtRequest.Latitude));
        Assert.Contains("-90", error.ErrorMessage);
    }

    [Theory]
    [InlineData(181)]
    [InlineData(-181)]
    public void Longitude_out_of_range_is_rejected(double longitude)
    {
        var result = _validator.Validate(Request(latitude: 43.8563, longitude: longitude));

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors, e => e.PropertyName == nameof(CreateCourtRequest.Longitude));
        Assert.Contains("-180", error.ErrorMessage);
    }
}
