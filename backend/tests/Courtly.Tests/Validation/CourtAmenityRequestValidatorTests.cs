using Courtly.Application.Common.Validation.Courts;
using Courtly.Contracts.Court;
using Xunit;

namespace Courtly.Tests.Validation;

/// <summary>
/// Feature 11 DoD (auto): the replace-the-whole-set amenity request is validated server-side — every AmenityId is a
/// real selection (&gt; 0), the ids are DISTINCT (a duplicate would violate the unique(CourtId, AmenityId) index),
/// and each Note stays within 300 characters. Existence of each amenity is a service-level 404, not validated here.
/// </summary>
public class CourtAmenityRequestValidatorTests
{
    private readonly SetCourtAmenitiesRequestValidator _validator = new();

    private static SetCourtAmenitiesRequest Request(params CourtAmenityInput[] amenities) =>
        new(amenities);

    private static CourtAmenityInput Input(long amenityId, string? note = null, bool isHighlighted = false) =>
        new(amenityId, note, isHighlighted);

    [Fact]
    public void Valid_set_passes()
    {
        var result = _validator.Validate(Request(Input(1, "Free parking"), Input(2, isHighlighted: true), Input(3)));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Empty_set_is_valid()
    {
        Assert.True(_validator.Validate(Request()).IsValid);
    }

    [Fact]
    public void Duplicate_amenity_ids_are_rejected()
    {
        var result = _validator.Validate(Request(Input(1), Input(2), Input(1)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("once"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Non_positive_amenity_id_is_rejected(long amenityId)
    {
        var result = _validator.Validate(Request(Input(amenityId)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("valid amenity"));
    }

    [Fact]
    public void Overlong_note_is_rejected_with_the_limit()
    {
        var result = _validator.Validate(Request(Input(1, note: new string('x', 301))));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("300"));
    }
}
