using Courtly.Application.Common.Validation;
using Courtly.Contracts.Auth;
using Xunit;

namespace Courtly.Tests.Validation;

/// <summary>Feature 6 DoD (auto): one validator — register input is validated server-side with explicit,
/// format-stating messages (rubric §4).</summary>
public class RegisterRequestValidatorTests
{
    private readonly RegisterRequestValidator _validator = new();

    private static RegisterRequest Request(string email = "user@example.com", string password = "Passw0rd!",
        string firstName = "Ada", string lastName = "Lovelace") =>
        new(email, password, firstName, lastName, null);

    [Fact]
    public void Valid_request_passes()
    {
        Assert.True(_validator.Validate(Request()).IsValid);
    }

    [Fact]
    public void Invalid_email_is_rejected_with_a_format_message()
    {
        var result = _validator.Validate(Request(email: "not-an-email"));

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors, e => e.PropertyName == nameof(RegisterRequest.Email));
        Assert.Contains("valid email", error.ErrorMessage);
    }

    [Theory]
    [InlineData("short1A")]       // too short
    [InlineData("alllowercase1")] // no uppercase
    [InlineData("ALLUPPERCASE1")] // no lowercase
    [InlineData("NoDigitsHere")]  // no digit
    public void Weak_password_is_rejected(string password)
    {
        var result = _validator.Validate(Request(password: password));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RegisterRequest.Password));
    }

    [Fact]
    public void Missing_names_are_rejected()
    {
        var result = _validator.Validate(Request(firstName: "", lastName: ""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RegisterRequest.FirstName));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RegisterRequest.LastName));
    }
}
