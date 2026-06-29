using System.Security.Cryptography;
using System.Text;
using Courtly.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Courtly.Api.Authorization;

/// <summary>
/// Verifies the <see cref="InternalKeyRequirement"/> (feature 18): the request must carry the shared internal push
/// secret in the <c>X-Internal-Key</c> header. The comparison is constant-time
/// (<see cref="CryptographicOperations.FixedTimeEquals{T}"/> over the UTF-8 bytes, with a length pre-check so a
/// mismatched length neither throws nor leaks timing), so the secret cannot be probed byte-by-byte. Stateless and
/// thread-safe — registered as a singleton <see cref="IAuthorizationHandler"/>.
/// </summary>
public sealed class InternalKeyAuthorizationHandler : AuthorizationHandler<InternalKeyRequirement>
{
    private const string HeaderName = "X-Internal-Key";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOptions<ApiOptions> _apiOptions;

    public InternalKeyAuthorizationHandler(IHttpContextAccessor httpContextAccessor, IOptions<ApiOptions> apiOptions)
    {
        _httpContextAccessor = httpContextAccessor;
        _apiOptions = apiOptions;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, InternalKeyRequirement requirement)
    {
        var expected = _apiOptions.Value.InternalPushKey;
        var presented = _httpContextAccessor.HttpContext?.Request.Headers[HeaderName].ToString();

        // A missing/empty configured key can never be matched — never authorize against a blank secret.
        if (!string.IsNullOrEmpty(expected)
            && !string.IsNullOrEmpty(presented)
            && FixedTimeEquals(expected, presented))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    /// <summary>Constant-time string comparison: the length pre-check keeps <see cref="CryptographicOperations"/> from
    /// throwing on differing-length spans, and the comparison itself does not short-circuit.</summary>
    private static bool FixedTimeEquals(string expected, string presented)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var presentedBytes = Encoding.UTF8.GetBytes(presented);

        return expectedBytes.Length == presentedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, presentedBytes);
    }
}
