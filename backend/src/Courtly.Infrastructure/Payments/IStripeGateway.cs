namespace Courtly.Infrastructure.Payments;

/// <summary>
/// The seam over the Stripe SDK (feature 16). It lives in <c>Courtly.Infrastructure</c> (beside the other outbound
/// integrations) so the Application layer — and the unit tests — depend only on this interface: the real Stripe.NET
/// calls and the webhook HMAC verification are isolated in <see cref="StripeGateway"/> and replaced by a fake in tests.
/// The gateway is intentionally thin and stateless: it talks to Stripe and returns plain records, holding no business
/// rules (idempotency, ownership, the reservation transition all live in the service).
/// </summary>
public interface IStripeGateway
{
    /// <summary>Creates a Stripe PaymentIntent for <paramref name="amountCents"/> in <paramref name="currency"/>. The
    /// <paramref name="idempotencyKey"/> makes a retried create return the same intent (no duplicate charge).</summary>
    Task<PaymentIntentResult> CreatePaymentIntentAsync(
        long amountCents, string currency, string idempotencyKey,
        IReadOnlyDictionary<string, string> metadata, CancellationToken ct = default);

    /// <summary>Re-reads an existing PaymentIntent (used to return the same client secret on a retried
    /// <c>/intent</c> for a still-Pending payment).</summary>
    Task<PaymentIntentResult> GetPaymentIntentAsync(string paymentIntentId, CancellationToken ct = default);

    /// <summary>Verifies the webhook payload's HMAC signature against the configured signing secret and parses it into
    /// a provider-agnostic <see cref="StripeWebhookEvent"/>. Throws on a missing/invalid signature so the controller
    /// returns 400 (the client never records success — rubric §7.1).</summary>
    StripeWebhookEvent ConstructEvent(string payloadJson, string signatureHeader);

    /// <summary>Refunds <paramref name="amountCents"/> against the charge behind <paramref name="paymentIntentId"/>.
    /// The caller passes the actually-charged amount (rubric §7.1 — refund on the charged amount, never a recomputed
    /// price).</summary>
    Task<RefundResult> CreateRefundAsync(
        string paymentIntentId, long amountCents, string? reason, CancellationToken ct = default);
}

/// <summary>A created/fetched Stripe PaymentIntent: its id and the client secret the in-app PaymentSheet confirms
/// with.</summary>
public sealed record PaymentIntentResult(string Id, string ClientSecret);

/// <summary>A verified, parsed Stripe webhook event reduced to what the finalize flow needs: the event
/// <see cref="Type"/> (e.g. <c>payment_intent.succeeded</c>), the <see cref="PaymentIntentId"/> it concerns, and the
/// <see cref="AmountReceivedCents"/> Stripe actually captured.</summary>
public sealed record StripeWebhookEvent(string Type, string? PaymentIntentId, long? AmountReceivedCents);

/// <summary>A created Stripe refund: its id and status.</summary>
public sealed record RefundResult(string Id, string Status);

/// <summary>The Stripe webhook event types feature 16 acts on (kept here so the gateway and service don't repeat the
/// magic strings — rubric §3.4).</summary>
public static class StripeEventTypes
{
    public const string PaymentIntentSucceeded = "payment_intent.succeeded";
}

/// <summary>
/// Raised by <see cref="StripeGateway.ConstructEvent"/> when the webhook payload's signature is missing or doesn't
/// verify against the signing secret. It lives in Infrastructure (the Application layer can't see Stripe's own
/// exception type), and the payments service translates it into the app's 400 so the webhook caller gets a clean
/// rejection rather than a 500.
/// </summary>
public sealed class WebhookSignatureException : Exception
{
    public WebhookSignatureException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
