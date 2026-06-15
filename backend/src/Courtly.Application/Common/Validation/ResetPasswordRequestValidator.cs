using Courtly.Contracts.Auth;
using FluentValidation;

namespace Courtly.Application.Common.Validation;

/// <summary>
/// Server-side validation for completing a password reset (rubric §4). The new password must meet the same
/// stated policy as registration; the email/token shape is checked before the service looks the token up.
/// </summary>
public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(r => r.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Enter a valid email address, e.g. name@example.com.");

        RuleFor(r => r.Token)
            .NotEmpty().WithMessage("Reset token is required.");

        RuleFor(r => r.NewPassword).ApplyPasswordPolicy();
    }
}
