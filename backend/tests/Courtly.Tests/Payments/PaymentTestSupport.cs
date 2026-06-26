using Courtly.Application.Abstractions;
using Courtly.Contracts.Messaging;
using Courtly.Infrastructure.Payments;

namespace Courtly.Tests.Payments;

/// <summary>
/// A controllable <see cref="IStripeGateway"/> for the feature-16 unit tests: it records what it was asked to do
/// (so a test can assert "the refund used the charged amount") and returns canned results, so the payments engine is
/// exercised end-to-end without touching Stripe. <see cref="ConstructEvent"/> can be told to return a specific event
/// or to throw the signature failure.
/// </summary>
public sealed class FakeStripeGateway : IStripeGateway
{
    public string NextIntentId { get; set; } = "pi_test_123";
    public string NextClientSecret { get; set; } = "pi_test_123_secret";
    public string NextRefundId { get; set; } = "re_test_123";
    public string NextRefundStatus { get; set; } = "succeeded";

    public bool ThrowOnConstruct { get; set; }
    public StripeWebhookEvent? NextWebhookEvent { get; set; }

    public List<(long AmountCents, string Currency, string IdempotencyKey)> CreatedIntents { get; } = new();
    public List<(string IntentId, long AmountCents, string? Reason)> CreatedRefunds { get; } = new();

    public Task<PaymentIntentResult> CreatePaymentIntentAsync(
        long amountCents, string currency, string idempotencyKey,
        IReadOnlyDictionary<string, string> metadata, CancellationToken ct = default)
    {
        CreatedIntents.Add((amountCents, currency, idempotencyKey));
        return Task.FromResult(new PaymentIntentResult(NextIntentId, NextClientSecret));
    }

    public Task<PaymentIntentResult> GetPaymentIntentAsync(string paymentIntentId, CancellationToken ct = default) =>
        Task.FromResult(new PaymentIntentResult(paymentIntentId, NextClientSecret));

    public StripeWebhookEvent ConstructEvent(string payloadJson, string signatureHeader)
    {
        if (ThrowOnConstruct)
        {
            throw new WebhookSignatureException("The Stripe webhook signature could not be verified.");
        }

        return NextWebhookEvent
            ?? new StripeWebhookEvent(StripeEventTypes.PaymentIntentSucceeded, NextIntentId, 0);
    }

    public Task<RefundResult> CreateRefundAsync(
        string paymentIntentId, long amountCents, string? reason, CancellationToken ct = default)
    {
        CreatedRefunds.Add((paymentIntentId, amountCents, reason));
        return Task.FromResult(new RefundResult(NextRefundId, NextRefundStatus));
    }
}

/// <summary>Captures the payment events that would hit the bus (feature 17 wires the real publisher).</summary>
public sealed class FakePaymentEventPublisher : Courtly.Infrastructure.Messaging.IPaymentEventPublisher
{
    public List<PaymentEvent> Published { get; } = new();

    public Task PublishAsync(PaymentEvent paymentEvent, CancellationToken ct = default)
    {
        Published.Add(paymentEvent);
        return Task.CompletedTask;
    }
}

/// <summary>Captures the reservation events the payments engine raises (reservation.confirmed/cancelled).</summary>
public sealed class FakeReservationEventPublisher : Courtly.Infrastructure.Messaging.IReservationEventPublisher
{
    public List<ReservationEvent> Published { get; } = new();

    public Task PublishAsync(ReservationEvent reservationEvent, CancellationToken ct = default)
    {
        Published.Add(reservationEvent);
        return Task.CompletedTask;
    }
}

/// <summary>Fixed UTC clock for deterministic timestamps.</summary>
public sealed class TestClock : IClock
{
    public DateTime UtcNow { get; init; }
}

/// <summary>An in-memory current-user for ownership/role checks.</summary>
public sealed class TestCurrentUser : ICurrentUser
{
    public Guid? UserId { get; init; }
    public string? Email => null;
    public string? Jti => null;
    public DateTime? AccessTokenExpiresAtUtc => null;
    public bool IsAuthenticated => UserId.HasValue;
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    public bool IsInRole(string role) => Roles.Contains(role);
}
