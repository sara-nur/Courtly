using Courtly.Contracts.SearchHistory;
using FluentValidation;

namespace Courtly.Application.Common.Validation.SearchHistory;

/// <summary>
/// Server-side validation for the search-history capture (feature 23 recommender signal). Discovered via the existing
/// assembly scan (<c>AddValidatorsFromAssemblyContaining&lt;RegisterRequestValidator&gt;</c>) — no per-validator
/// registration. The raw query is length-capped (the service also trims + truncates as a backstop); prices must be
/// non-negative when supplied and the min must not exceed the max when both are supplied. The reference ids are not
/// existence-checked here (they are optional, nullable filter hints, not foreign keys the user must satisfy).
/// </summary>
public sealed class RecordSearchRequestValidator : AbstractValidator<RecordSearchRequest>
{
    /// <summary>Upper bound on the stored raw query text (mirrors <c>SearchHistoryService</c>).</summary>
    private const int MaxRawQueryLength = 300;

    public RecordSearchRequestValidator()
    {
        RuleFor(r => r.RawQuery)
            .MaximumLength(MaxRawQueryLength)
            .WithMessage($"The search text must be at most {MaxRawQueryLength} characters.");

        RuleFor(r => r.MinPrice)
            .GreaterThanOrEqualTo(0)
            .When(r => r.MinPrice.HasValue)
            .WithMessage("The minimum price must not be negative.");

        RuleFor(r => r.MaxPrice)
            .GreaterThanOrEqualTo(0)
            .When(r => r.MaxPrice.HasValue)
            .WithMessage("The maximum price must not be negative.");

        RuleFor(r => r.MinPrice)
            .LessThanOrEqualTo(r => r.MaxPrice)
            .When(r => r.MinPrice.HasValue && r.MaxPrice.HasValue)
            .WithMessage("The minimum price must not exceed the maximum price.");
    }
}
