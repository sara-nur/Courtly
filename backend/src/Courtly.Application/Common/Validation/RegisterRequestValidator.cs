using Courtly.Contracts.Auth;
using Courtly.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Courtly.Application.Common.Validation;

/// <summary>
/// Server-side validation for self-registration (rubric §3.4 + §4). Runs in the FluentValidation pipeline
/// before the service; messages spell out the exact format and limits so the Flutter client can show them
/// below the offending field. Discovered via assembly scan — no per-validator registration. Resolved per request,
/// so it can take the scoped <see cref="CourtlyDbContext"/> to check the optional city foreign key.
/// </summary>
public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    private const int MaxNameLength = 100;

    public RegisterRequestValidator(CourtlyDbContext db)
    {
        RuleFor(r => r.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Enter a valid email address, e.g. name@example.com.");

        RuleFor(r => r.Password).ApplyPasswordPolicy();

        RuleFor(r => r.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(MaxNameLength).WithMessage($"First name must be at most {MaxNameLength} characters.");

        RuleFor(r => r.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(MaxNameLength).WithMessage($"Last name must be at most {MaxNameLength} characters.");

        // CityId is optional, but when supplied it must reference a real city. Validating it here turns a bad id into
        // a clean 400 with a field-keyed message, instead of a DB foreign-key exception when RegisterAsync sets it.
        RuleFor(r => r.CityId)
            .MustAsync(async (cityId, ct) => await db.Cities.AnyAsync(c => c.Id == cityId!.Value, ct))
            .When(r => r.CityId.HasValue)
            .WithMessage("The selected city does not exist.");
    }
}
