using Courtly.Contracts.Messaging;

namespace Courtly.Infrastructure.Messaging;

/// <summary>
/// The seam through which the payments engine (feature 16) raises payment lifecycle events. Mirrors
/// <see cref="IReservationEventPublisher"/>: it lives in <c>Courtly.Infrastructure</c> so the Application layer depends
/// only on the interface, and feature 17 can swap the logging stub for the real RabbitMQ topic-exchange publisher
/// without touching a single caller.
/// </summary>
public interface IPaymentEventPublisher
{
    /// <summary>Publishes one payment lifecycle event. Called AFTER the payment change has been committed, so a publish
    /// failure can never roll back a persisted finalize/refund.</summary>
    Task PublishAsync(PaymentEvent paymentEvent, CancellationToken ct = default);
}
