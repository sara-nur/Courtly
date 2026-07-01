using Courtly.Contracts.Reviews;
using FluentValidation;

namespace Courtly.Application.Common.Validation.Reviews;

/// <summary>
/// Server-side validation for the court-review write (feature 24). Discovered via the existing assembly scan
/// (<c>AddValidatorsFromAssemblyContaining&lt;RegisterRequestValidator&gt;</c>) — no per-validator registration. The
/// rating is a 1–5 star value; the comment is optional and length-capped to match the entity column
/// (<c>Review.Comment</c> = 2000). The reservation id is a positive key; ownership / completed / one-per-reservation are
/// business rules enforced in <c>ReviewService</c> (they need the database), not here.
/// </summary>
public sealed class CreateReviewRequestValidator : AbstractValidator<CreateReviewRequest>
{
    /// <summary>Lowest valid star rating.</summary>
    private const int MinRating = 1;

    /// <summary>Highest valid star rating.</summary>
    private const int MaxRating = 5;

    /// <summary>Upper bound on the stored comment text (mirrors <c>ReviewConfiguration</c>).</summary>
    private const int MaxCommentLength = 2000;

    public CreateReviewRequestValidator()
    {
        RuleFor(r => r.ReservationId)
            .GreaterThan(0)
            .WithMessage("A reservation is required.");

        RuleFor(r => r.Rating)
            .InclusiveBetween(MinRating, MaxRating)
            .WithMessage($"The rating must be between {MinRating} and {MaxRating} stars.");

        RuleFor(r => r.Comment)
            .MaximumLength(MaxCommentLength)
            .WithMessage($"The comment must be at most {MaxCommentLength} characters.");
    }
}
