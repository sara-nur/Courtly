namespace Courtly.Infrastructure.Configuration;

/// <summary>
/// API self-reference settings. <c>BaseUrl</c> is the public API URL; <c>InternalPushKey</c> is the
/// shared secret the Worker uses to call the API's internal SignalR push endpoint (feature 18).
/// Bound from the <c>API_BASE_URL</c> / <c>INTERNAL_PUSH_KEY</c> env keys.
/// </summary>
public sealed class ApiOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string InternalPushKey { get; set; } = string.Empty;
}
