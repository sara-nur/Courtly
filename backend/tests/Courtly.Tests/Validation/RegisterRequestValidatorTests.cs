using Courtly.Application.Common.Validation;
using Courtly.Contracts.Auth;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Courtly.Tests.Validation;

/// <summary>Feature 6 DoD (auto): one validator — register input is validated server-side with explicit,
/// format-stating messages (rubric §4). CityId is an optional foreign key, existence-checked against the DB when
/// supplied, so the validator now runs asynchronously (uses an in-memory context).</summary>
public class RegisterRequestValidatorTests
{
    private readonly CourtlyDbContext _db = new(
        new DbContextOptionsBuilder<CourtlyDbContext>()
            .UseInMemoryDatabase($"register-validator-{Guid.NewGuid()}")
            .Options);

    private RegisterRequestValidator NewValidator() => new(_db);

    private static RegisterRequest Request(string email = "user@example.com", string password = "Passw0rd!",
        string firstName = "Ada", string lastName = "Lovelace", long? cityId = null) =>
        new(email, password, firstName, lastName, cityId);

    [Fact]
    public async Task Valid_request_passes()
    {
        Assert.True((await NewValidator().ValidateAsync(Request())).IsValid);
    }

    [Fact]
    public async Task Invalid_email_is_rejected_with_a_format_message()
    {
        var result = await NewValidator().ValidateAsync(Request(email: "not-an-email"));

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors, e => e.PropertyName == nameof(RegisterRequest.Email));
        Assert.Contains("valid email", error.ErrorMessage);
    }

    [Theory]
    [InlineData("short1A")]       // too short
    [InlineData("alllowercase1")] // no uppercase
    [InlineData("ALLUPPERCASE1")] // no lowercase
    [InlineData("NoDigitsHere")]  // no digit
    public async Task Weak_password_is_rejected(string password)
    {
        var result = await NewValidator().ValidateAsync(Request(password: password));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RegisterRequest.Password));
    }

    [Fact]
    public async Task Missing_names_are_rejected()
    {
        var result = await NewValidator().ValidateAsync(Request(firstName: "", lastName: ""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RegisterRequest.FirstName));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RegisterRequest.LastName));
    }

    [Fact]
    public async Task Unknown_city_is_rejected()
    {
        // No cities are seeded, so any supplied id fails the existence check with a field-keyed message.
        var result = await NewValidator().ValidateAsync(Request(cityId: 999_999));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RegisterRequest.CityId));
    }
}
