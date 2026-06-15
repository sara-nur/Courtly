using Courtly.Application.Common.Errors;
using Courtly.Application.Common.Exceptions;
using Xunit;

namespace Courtly.Tests.Errors;

/// <summary>Feature 6 DoD (auto): exception → HTTP status + standardized ErrorResponse mapping, with no
/// internal detail leaked outside Development.</summary>
public class ErrorResponseFactoryTests
{
    public static IEnumerable<object[]> AppExceptions() => new[]
    {
        new object[] { new UnauthorizedException("boom"), 401 },
        new object[] { new ForbiddenException("boom"), 403 },
        new object[] { new NotFoundException("boom"), 404 },
        new object[] { new ConflictException("boom"), 409 },
        new object[] { new BusinessException("boom"), 409 },
    };

    [Theory]
    [MemberData(nameof(AppExceptions))]
    public void Maps_app_exception_to_its_status_and_message(AppException exception, int expectedStatus)
    {
        var (status, body) = ErrorResponseFactory.Create(exception, includeDetails: false, traceId: "trace-1");

        Assert.Equal(expectedStatus, status);
        Assert.Equal(expectedStatus, body.Status);
        Assert.Equal("boom", body.Title);
        Assert.Equal("trace-1", body.TraceId);
        Assert.Null(body.Detail);
    }

    [Fact]
    public void Validation_exception_maps_to_400_and_carries_field_errors()
    {
        var errors = new Dictionary<string, string[]> { ["Email"] = ["Enter a valid email."] };
        var exception = new ValidationException("One or more validation errors occurred.", errors);

        var (status, body) = ErrorResponseFactory.Create(exception, includeDetails: false, traceId: null);

        Assert.Equal(400, status);
        Assert.NotNull(body.Errors);
        Assert.Equal(["Enter a valid email."], body.Errors!["Email"]);
    }

    [Fact]
    public void Unknown_exception_becomes_generic_500_without_detail_outside_development()
    {
        var (status, body) = ErrorResponseFactory.Create(
            new InvalidOperationException("secret internals"), includeDetails: false, traceId: "t");

        Assert.Equal(500, status);
        Assert.Equal("An unexpected error occurred.", body.Title);
        Assert.Null(body.Detail);
        Assert.DoesNotContain("secret internals", body.Title);
    }

    [Fact]
    public void Unknown_exception_includes_detail_in_development()
    {
        var (_, body) = ErrorResponseFactory.Create(
            new InvalidOperationException("secret internals"), includeDetails: true, traceId: "t");

        Assert.NotNull(body.Detail);
        Assert.Contains("secret internals", body.Detail);
    }
}
