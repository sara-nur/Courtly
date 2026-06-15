namespace Courtly.Infrastructure.Configuration;

/// <summary>
/// API self-reference settings. <c>BaseUrl</c> is the public API URL; <c>InternalPushKey</c> is the
/// shared secret the Worker uses to call the API's internal SignalR push endpoint (feature 18);
/// <c>AllowedCorsOrigins</c> is the explicit allow-list for the single CORS policy (feature 6).
/// Bound from the <c>API_BASE_URL</c> / <c>INTERNAL_PUSH_KEY</c> / <c>CORS_ALLOWED_ORIGINS</c> env keys.
/// </summary>
public sealed class ApiOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string InternalPushKey { get; set; } = string.Empty;

    /// <summary>Origins permitted by CORS (rubric §3.4: configure once, list origins explicitly — never a
    /// blanket allow-any). Parsed from the comma-separated <c>CORS_ALLOWED_ORIGINS</c> env key.</summary>
    public string[] AllowedCorsOrigins { get; set; } = [];
}
