using Courtly.Contracts.Users;
using Courtly.Domain.Constants;
using FluentValidation;

namespace Courtly.Application.Common.Validation;

/// <summary>
/// Server-side validation for an admin role assignment (feature 15A). The role must be one of the canonical
/// <see cref="Roles.All"/> names; the message lists the allowed values so the client can render it. Discovered via the
/// existing assembly scan — no per-validator registration. The self-role-change guard is a business rule enforced in
/// <c>UserService</c> (it needs the acting user), not here.
/// </summary>
public sealed class AssignRoleRequestValidator : AbstractValidator<AssignRoleRequest>
{
    public AssignRoleRequestValidator()
    {
        RuleFor(r => r.Role)
            .NotEmpty().WithMessage("Role is required.")
            .Must(role => Roles.All.Contains(role))
            .WithMessage($"Role must be one of: {string.Join(", ", Roles.All)}.");
    }
}
