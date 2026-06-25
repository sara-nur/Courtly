using Courtly.Application.Common.Validation.Courts;
using Courtly.Contracts.Court;
using Xunit;

namespace Courtly.Tests.Validation;

/// <summary>
/// Feature 12: server-side validation for opening a maintenance window. The reason is required (≤500 chars) and,
/// when both ends are supplied, the end must be strictly after the start (the "end after now" case where the start
/// is omitted is the service's job, against the server clock).
/// </summary>
public class MaintenanceRequestValidatorTests
{
    private static readonly DateTime Start = new(2026, 6, 25, 12, 0, 0, DateTimeKind.Utc);

    private readonly CreateMaintenanceRequestValidator _validator = new();

    [Fact]
    public void Reason_only_is_valid()
    {
        var result = _validator.Validate(new CreateMaintenanceRequest("Resurfacing the clay court"));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Empty_reason_fails_with_a_clear_message()
    {
        var result = _validator.Validate(new CreateMaintenanceRequest("   "));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("reason", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Reason_over_500_chars_fails()
    {
        var result = _validator.Validate(new CreateMaintenanceRequest(new string('x', 501)));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void End_before_start_fails_when_both_supplied()
    {
        var result = _validator.Validate(
            new CreateMaintenanceRequest("Window", StartUtc: Start, EndUtc: Start.AddHours(-1)));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("after", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void End_after_start_is_valid()
    {
        var result = _validator.Validate(
            new CreateMaintenanceRequest("Window", StartUtc: Start, EndUtc: Start.AddHours(2)));
        Assert.True(result.IsValid);
    }
}
