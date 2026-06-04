namespace Courtly.Infrastructure.Configuration;

/// <summary>Stripe sandbox keys (consumed by feature 16: payments). Bound from the <c>STRIPE_*</c> env keys.</summary>
public sealed class StripeOptions
{
    public string SecretKey { get; set; } = string.Empty;
    public string PublishableKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
}
