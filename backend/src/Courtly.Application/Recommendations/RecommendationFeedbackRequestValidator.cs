using Courtly.Contracts.Common;
using Courtly.Contracts.Recommendations;
using FluentValidation;

namespace Courtly.Application.Recommendations;

/// <summary>
/// Server-side validation for the "Are these recommendations helpful?" Yes/No (feature 29). At least one court must
/// be rated, and a single request cannot exceed <see cref="PaginationQuery.MaxPageSize"/> courts (the same hard list
/// bound the rest of the API uses — rubric §8.2). Auto-discovered by the assembly scan and run by the global
/// <c>ValidationActionFilter</c>, so a bad request becomes a standardized 400 with a clear message.
/// </summary>
public sealed class RecommendationFeedbackRequestValidator : AbstractValidator<RecommendationFeedbackRequest>
{
    public RecommendationFeedbackRequestValidator()
    {
        RuleFor(r => r.CourtIds)
            .NotEmpty().WithMessage("Select at least one court to rate.")
            .Must(ids => ids is null || ids.Count <= PaginationQuery.MaxPageSize)
            .WithMessage($"You can rate at most {PaginationQuery.MaxPageSize} courts at once.");

        RuleForEach(r => r.CourtIds)
            .GreaterThan(0).WithMessage("Court ids must be positive.");
    }
}
