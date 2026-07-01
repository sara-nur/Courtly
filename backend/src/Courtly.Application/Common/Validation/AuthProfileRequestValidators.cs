using Courtly.Contracts.Auth;
using FluentValidation;

namespace Courtly.Application.Common.Validation;

/// <summary>
/// Server-side validation for the self-service profile endpoints (feature 28, rubric §4). Messages spell
/// out the exact constraint so the Flutter client renders them below the offending field. Discovered via
/// assembly scan — no per-validator registration. City existence and email uniqueness are enforced in the
/// service (they need the DbContext); these validators only cover the request-shape rules.
/// </summary>
public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    private const int MaxNameLength = 100;

    public UpdateProfileRequestValidator()
    {
        RuleFor(r => r.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Enter a valid email address, e.g. name@example.com.");

        RuleFor(r => r.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(MaxNameLength).WithMessage($"First name must be at most {MaxNameLength} characters.");

        RuleFor(r => r.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(MaxNameLength).WithMessage($"Last name must be at most {MaxNameLength} characters.");
    }
}

/// <summary>
/// Validation for a self-service password change (rubric §294 — the user confirms the current password).
/// The new password reuses the shared <see cref="PasswordRuleExtensions.ApplyPasswordPolicy{T}"/> so it
/// states the same rule as register/reset; Identity re-validates it server-side as the authority.
/// </summary>
public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(r => r.CurrentPassword)
            .NotEmpty().WithMessage("Current password is required.");

        RuleFor(r => r.NewPassword).ApplyPasswordPolicy();
    }
}
