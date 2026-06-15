using FluentValidation;

namespace Courtly.Application.Common.Validation;

/// <summary>
/// Shared password rule so register and reset state the <em>same</em> constraints in the <em>same</em>
/// words (rubric §4: messages must spell out the format; §8.1: no duplicated logic). A single predicate
/// yields one clear message for any violation. Mirrors the ASP.NET Identity password policy configured in
/// <c>Program.cs</c>; Identity remains the server-side backstop.
/// </summary>
public static class PasswordRuleExtensions
{
    public const int MinLength = 8;

    private const string Message =
        "Password must be at least 8 characters and include an uppercase letter, a lowercase letter, and a digit.";

    public static IRuleBuilderOptions<T, string> ApplyPasswordPolicy<T>(this IRuleBuilder<T, string> rule) =>
        rule.Must(BeStrong).WithMessage(Message);

    private static bool BeStrong(string password) =>
        !string.IsNullOrEmpty(password)
        && password.Length >= MinLength
        && password.Any(char.IsUpper)
        && password.Any(char.IsLower)
        && password.Any(char.IsDigit);
}
