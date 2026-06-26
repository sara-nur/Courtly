using Courtly.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;

namespace Courtly.Infrastructure.Payments;

/// <summary>
/// The Stripe.NET implementation of <see cref="IStripeGateway"/> (feature 16). Holds a single <see cref="StripeClient"/>
/// built from the secret key and the webhook signing secret — both read once from <see cref="StripeOptions"/> in the
/// constructor (rubric §8.2: read env once), never re-read per call. Stateless beyond those, so it's registered as a
/// singleton. All business rules (idempotency, ownership, the reservation transition) live in the service; this type
/// only talks to Stripe and returns provider-agnostic records.
/// </summary>
public sealed class StripeGateway : IStripeGateway
{
    private readonly StripeClient _client;
    private readonly string _webhookSecret;
    private readonly ILogger<StripeGateway> _logger;

    public StripeGateway(IOptions<StripeOptions> options, ILogger<StripeGateway> logger)
    {
        var stripe = options.Value;
        _client = new StripeClient(stripe.SecretKey);
        _webhookSecret = stripe.WebhookSecret;
        _logger = logger;
    }

    public async Task<PaymentIntentResult> CreatePaymentIntentAsync(
        long amountCents, string currency, string idempotencyKey,
        IReadOnlyDictionary<string, string> metadata, CancellationToken ct = default)
    {
        var service = new PaymentIntentService(_client);
        var createOptions = new PaymentIntentCreateOptions
        {
            Amount = amountCents,
            Currency = currency,
            Metadata = new Dictionary<string, string>(metadata),
            // Card payments via the in-app PaymentSheet (feature 26); disallow redirect-based methods so the intent is
            // confirmable without a return_url (also lets the Stripe CLI confirm it with a test card during review).
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
            {
                Enabled = true,
                AllowRedirects = "never",
            },
        };

        // The idempotency key makes a retried create return the same intent instead of charging twice.
        var requestOptions = new RequestOptions { IdempotencyKey = idempotencyKey };
        var intent = await service.CreateAsync(createOptions, requestOptions, ct);

        _logger.LogInformation(
            "Created Stripe PaymentIntent {IntentId} for {Amount} {Currency}.", intent.Id, amountCents, currency);
        return new PaymentIntentResult(intent.Id, intent.ClientSecret ?? string.Empty);
    }

    public async Task<PaymentIntentResult> GetPaymentIntentAsync(string paymentIntentId, CancellationToken ct = default)
    {
        var service = new PaymentIntentService(_client);
        var intent = await service.GetAsync(paymentIntentId, cancellationToken: ct);
        return new PaymentIntentResult(intent.Id, intent.ClientSecret ?? string.Empty);
    }

    public StripeWebhookEvent ConstructEvent(string payloadJson, string signatureHeader)
    {
        try
        {
            // Verifies the HMAC signature against the signing secret; throws StripeException on a bad/missing signature.
            // throwOnApiVersionMismatch: false so a difference between the account's API version and the SDK's pinned
            // version doesn't reject an otherwise-valid event.
            var stripeEvent = EventUtility.ConstructEvent(
                payloadJson, signatureHeader, _webhookSecret, throwOnApiVersionMismatch: false);

            var intent = stripeEvent.Data?.Object as PaymentIntent;
            return new StripeWebhookEvent(stripeEvent.Type, intent?.Id, intent?.AmountReceived);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Rejected a Stripe webhook with an invalid signature.");
            throw new WebhookSignatureException("The Stripe webhook signature could not be verified.", ex);
        }
    }

    public async Task<RefundResult> CreateRefundAsync(
        string paymentIntentId, long amountCents, string? reason, CancellationToken ct = default)
    {
        var service = new RefundService(_client);
        var createOptions = new RefundCreateOptions
        {
            PaymentIntent = paymentIntentId,
            Amount = amountCents,
            // Stripe's Reason only accepts a fixed set of codes — the admin's free-text reason rides in metadata.
            Reason = "requested_by_customer",
            Metadata = string.IsNullOrWhiteSpace(reason)
                ? null
                : new Dictionary<string, string> { ["reason"] = reason },
        };

        var refund = await service.CreateAsync(createOptions, cancellationToken: ct);

        _logger.LogInformation(
            "Created Stripe refund {RefundId} ({Amount} cents) for intent {IntentId}.",
            refund.Id, amountCents, paymentIntentId);
        return new RefundResult(refund.Id, refund.Status ?? string.Empty);
    }
}
