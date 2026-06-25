using Courtly.Contracts.Court;
using FluentValidation;

namespace Courtly.Application.Common.Validation.Courts;

/// <summary>
/// Server-side validation for generating a court's time slots (feature 13). Messages spell out the exact limits so the
/// Flutter client can render them below the offending field. Discovered via the existing assembly scan — no
/// per-validator registration. The server still owns the price and bucket; this only guards the user-supplied window:
/// a sane date range (within <see cref="MaxRangeDays"/>), a daily open/close window, and a slot length that fits.
/// </summary>
public sealed class GenerateSlotsRequestValidator : AbstractValidator<GenerateSlotsRequest>
{
    // Bound the generation horizon so an admin can't materialize years of rows in one request (rolling-window guidance).
    private const int MaxRangeDays = 60;
    private const int MinHour = 0;
    private const int MaxHour = 24;
    private const decimal MinPeak = 1.0m;
    private const decimal MaxPeak = 5.0m;

    /// <summary>Allowed slot lengths in minutes (whole/half/quarter-hour blocks).</summary>
    private static readonly int[] AllowedSlotMinutes = { 30, 60, 90, 120 };

    public GenerateSlotsRequestValidator()
    {
        RuleFor(r => r.ToDate)
            .GreaterThanOrEqualTo(r => r.FromDate)
            .WithMessage("The end date must be on or after the start date.");

        RuleFor(r => r)
            .Must(r => r.ToDate.DayNumber - r.FromDate.DayNumber + 1 <= MaxRangeDays)
            .WithMessage($"The date range must be at most {MaxRangeDays} days.")
            .WithName(nameof(GenerateSlotsRequest.ToDate));

        RuleFor(r => r.OpenHour)
            .InclusiveBetween(MinHour, MaxHour - 1)
            .WithMessage($"The open hour must be between {MinHour} and {MaxHour - 1}.");

        RuleFor(r => r.CloseHour)
            .InclusiveBetween(MinHour + 1, MaxHour)
            .WithMessage($"The close hour must be between {MinHour + 1} and {MaxHour}.");

        RuleFor(r => r.CloseHour)
            .GreaterThan(r => r.OpenHour)
            .WithMessage("The close hour must be after the open hour.");

        RuleFor(r => r.SlotMinutes)
            .Must(m => AllowedSlotMinutes.Contains(m))
            .WithMessage($"The slot length must be one of: {string.Join(", ", AllowedSlotMinutes)} minutes.");

        // At least one whole slot must fit inside the daily window.
        RuleFor(r => r)
            .Must(r => (r.CloseHour - r.OpenHour) * 60 >= r.SlotMinutes)
            .WithMessage("The daily window is shorter than one slot — widen the hours or shorten the slot length.")
            .WithName(nameof(GenerateSlotsRequest.SlotMinutes));

        RuleFor(r => r.EveningPeakMultiplier)
            .Must(v => v >= MinPeak && v <= MaxPeak)
            .When(r => r.EveningPeakMultiplier.HasValue)
            .WithMessage($"The evening peak multiplier must be between {MinPeak:0.0} and {MaxPeak:0.0}.");
    }
}
