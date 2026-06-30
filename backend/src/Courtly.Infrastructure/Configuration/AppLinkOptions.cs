namespace Courtly.Infrastructure.Configuration;

/// <summary>
/// Password-reset link configuration (feature 22). <c>ResetPasswordUrl</c> is the base of the reset
/// link emailed to the user — the sender appends <c>?email=…&amp;token=…</c>, and clicking it opens the
/// browser reset page served at <c>GET /reset-password</c>. Bound from the <c>APP_RESET_PASSWORD_URL</c>
/// env key (rubric §3.3: config in <c>.env</c>, never hardcoded), e.g.
/// <c>http://localhost:5000/reset-password</c>.
/// </summary>
public sealed class AppLinkOptions
{
    public string ResetPasswordUrl { get; set; } = string.Empty;
}
