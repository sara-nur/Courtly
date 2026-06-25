using Courtly.Application.Common.Validation.Courts;
using Courtly.Contracts.Court;
using Xunit;

namespace Courtly.Tests.Validation;

/// <summary>
/// Feature 13: server-side validation for the slot generation request. The server still owns price + bucket; this only
/// guards the user-supplied window — a sane date range (within the 60-day horizon), a valid daily open/close window, a
/// slot length from the allowed set that fits inside the window, and (when supplied) a peak multiplier in range.
/// </summary>
public class SlotRequestValidatorTests
{
    private static readonly DateOnly From = new(2026, 6, 25);

    private readonly GenerateSlotsRequestValidator _validator = new();

    private static GenerateSlotsRequest Request(
        DateOnly? from = null, DateOnly? to = null,
        int openHour = 8, int closeHour = 20, int slotMinutes = 60, decimal? peak = null) =>
        new(from ?? From, to ?? From, openHour, closeHour, slotMinutes, peak);

    [Fact]
    public void A_well_formed_request_is_valid()
    {
        Assert.True(_validator.Validate(Request()).IsValid);
    }

    [Fact]
    public void End_date_before_start_date_fails()
    {
        var result = _validator.Validate(Request(from: From, to: From.AddDays(-1)));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Range_over_60_days_fails()
    {
        var result = _validator.Validate(Request(from: From, to: From.AddDays(60)));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("60", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Exactly_60_days_is_valid()
    {
        Assert.True(_validator.Validate(Request(from: From, to: From.AddDays(59))).IsValid); // inclusive span = 60
    }

    [Fact]
    public void Close_hour_not_after_open_hour_fails()
    {
        Assert.False(_validator.Validate(Request(openHour: 20, closeHour: 8)).IsValid);
    }

    [Fact]
    public void Open_hour_out_of_range_fails()
    {
        Assert.False(_validator.Validate(Request(openHour: 24, closeHour: 24)).IsValid);
    }

    [Fact]
    public void Disallowed_slot_length_fails()
    {
        var result = _validator.Validate(Request(slotMinutes: 45));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("slot length", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Slot_longer_than_the_window_fails()
    {
        // 08:00–09:00 is one hour; a 120-min slot can't fit.
        var result = _validator.Validate(Request(openHour: 8, closeHour: 9, slotMinutes: 120));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Peak_multiplier_below_one_fails()
    {
        Assert.False(_validator.Validate(Request(peak: 0.5m)).IsValid);
    }

    [Fact]
    public void Peak_multiplier_in_range_is_valid()
    {
        Assert.True(_validator.Validate(Request(peak: 1.5m)).IsValid);
    }
}
