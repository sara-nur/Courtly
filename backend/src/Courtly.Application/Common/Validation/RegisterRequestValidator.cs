using Courtly.Contracts.Auth;
using FluentValidation;

namespace Courtly.Application.Common.Validation;

/// <summary>
/// Server-side validation for self-registration (rubric §3.4 + §4). Runs in the FluentValidation pipeline
/// before the service; messages spell out the exact format and limits so the Flutter client can show them
/// below the offending field. Discovered via assembly scan — no per-validator registration.
/// </summary>
public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    private const int MaxNameLength = 100;

    public RegisterRequestValidator()
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
    }
}
