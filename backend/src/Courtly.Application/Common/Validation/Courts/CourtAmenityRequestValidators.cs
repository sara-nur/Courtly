using Courtly.Contracts.Court;
using FluentValidation;

namespace Courtly.Application.Common.Validation.Courts;

/// <summary>
/// Server-side validation for the replace-the-whole-set amenity request (feature 11). The endpoint swaps the
/// court's entire amenity set for this one, so the request must be internally consistent: every AmenityId is a
/// real selection (&gt; 0), the ids are DISTINCT (a duplicate would violate the unique(CourtId, AmenityId) index),
/// and each Note stays within 300 characters. Existence of each amenity is a service-level concern (clean 404),
/// not validated here. Discovered via the assembly scan — no per-validator registration.
/// </summary>
public sealed class SetCourtAmenitiesRequestValidator : AbstractValidator<SetCourtAmenitiesRequest>
{
    private const int MaxNoteLength = 300;

    public SetCourtAmenitiesRequestValidator()
    {
        RuleForEach(x => x.Amenities).ChildRules(item =>
        {
            item.RuleFor(a => a.AmenityId)
                .GreaterThan(0).WithMessage("Please select a valid amenity.");

            item.RuleFor(a => a.Note)
                .MaximumLength(MaxNoteLength)
                .WithMessage($"Amenity note must be at most {MaxNoteLength} characters.");
        });

        RuleFor(x => x.Amenities)
            .Must(BeDistinctByAmenityId)
            .WithMessage("Each amenity can only be added once.");
    }

    /// <summary>True when no AmenityId repeats (or the list is empty/null) so the unique index is never hit.</summary>
    private static bool BeDistinctByAmenityId(IReadOnlyList<CourtAmenityInput>? amenities)
    {
        if (amenities is null || amenities.Count == 0)
        {
            return true;
        }

        var ids = amenities.Select(a => a.AmenityId).ToList();
        return ids.Count == ids.Distinct().Count();
    }
}
