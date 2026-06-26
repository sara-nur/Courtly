namespace Courtly.Infrastructure.Configuration;

/// <summary>Stripe sandbox keys (consumed by feature 16: payments). Bound from the <c>STRIPE_*</c> env keys.</summary>
public sealed class StripeOptions
{
    public string SecretKey { get; set; } = string.Empty;
    public string PublishableKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>ISO currency the server charges in (e.g. <c>usd</c>). The server owns the amount + currency — the
    /// client never sends them (rubric §7.1). Defaults to <c>usd</c> when <c>STRIPE_CURRENCY</c> is unset.</summary>
    public string Currency { get; set; } = "usd";
}
