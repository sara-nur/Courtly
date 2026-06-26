using Courtly.Contracts.Messaging;
using Microsoft.Extensions.Logging;

namespace Courtly.Infrastructure.Messaging;

/// <summary>
/// The feature-16 implementation of <see cref="IPaymentEventPublisher"/>: until the Worker + RabbitMQ publisher land in
/// feature 17, payment lifecycle events are LOGGED rather than dropped, so the payments engine stays event-aware and
/// "what would be published" is visible in the API logs. Feature 17 replaces only this registration with the real
/// topic-exchange publisher — every caller depends on the interface, so nothing else changes. Stateless (logger only),
/// hence safe to register as a singleton.
/// </summary>
public sealed class LoggingPaymentEventPublisher : IPaymentEventPublisher
{
    private readonly ILogger<LoggingPaymentEventPublisher> _logger;

    public LoggingPaymentEventPublisher(ILogger<LoggingPaymentEventPublisher> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync(PaymentEvent paymentEvent, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Payment event {RoutingKey}: payment {PaymentId} (reservation {ReservationId}, user {UserId}, amount {Amount}, charged {ChargedCents} cents).",
            paymentEvent.RoutingKey, paymentEvent.PaymentId, paymentEvent.ReservationId, paymentEvent.UserId,
            paymentEvent.Amount, paymentEvent.AmountChargedCents);
        return Task.CompletedTask;
    }
}
