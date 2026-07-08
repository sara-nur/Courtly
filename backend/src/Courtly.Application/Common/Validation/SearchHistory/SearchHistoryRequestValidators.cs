using Courtly.Contracts.SearchHistory;
using Courtly.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Courtly.Application.Common.Validation.SearchHistory;

/// <summary>
/// Server-side validation for the search-history capture (feature 23 recommender signal). Discovered via the existing
/// assembly scan (<c>AddValidatorsFromAssemblyContaining&lt;RegisterRequestValidator&gt;</c>) — no per-validator
/// registration. The raw query is length-capped (the service also trims + truncates as a backstop); prices must be
/// non-negative when supplied and the min must not exceed the max when both are supplied. <c>SurfaceTypeId</c> and
/// <c>CourtTypeId</c> ARE enforced foreign keys (see <c>SearchHistoryConfiguration</c>, <c>DeleteBehavior.Restrict</c>),
/// so when supplied they are existence-checked here — a bad id becomes a clean 400 rather than a DB foreign-key
/// exception at save. Resolved per request, so it can take the scoped <see cref="CourtlyDbContext"/>.
/// </summary>
public sealed class RecordSearchRequestValidator : AbstractValidator<RecordSearchRequest>
{
    /// <summary>Upper bound on the stored raw query text (mirrors <c>SearchHistoryService</c>).</summary>
    private const int MaxRawQueryLength = 300;

    public RecordSearchRequestValidator(CourtlyDbContext db)
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

        RuleFor(r => r.SurfaceTypeId)
            .MustAsync(async (id, ct) => await db.SurfaceTypes.AnyAsync(s => s.Id == id!.Value, ct))
            .When(r => r.SurfaceTypeId.HasValue)
            .WithMessage("The selected surface type does not exist.");

        RuleFor(r => r.CourtTypeId)
            .MustAsync(async (id, ct) => await db.CourtTypes.AnyAsync(c => c.Id == id!.Value, ct))
            .When(r => r.CourtTypeId.HasValue)
            .WithMessage("The selected court type does not exist.");
    }
}
