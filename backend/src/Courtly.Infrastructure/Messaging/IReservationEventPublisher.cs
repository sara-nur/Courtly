using Courtly.Contracts.Messaging;

namespace Courtly.Infrastructure.Messaging;

/// <summary>
/// The seam through which the reservation engine (feature 14) raises lifecycle events. It lives in
/// <c>Courtly.Infrastructure</c> (alongside the other outbound integrations) so the Application layer depends only on
/// the interface, and feature 17 can swap the implementation from the logging stub to the real RabbitMQ topic-exchange
/// publisher without touching a single caller.
/// </summary>
public interface IReservationEventPublisher
{
    /// <summary>Publishes one reservation lifecycle event. Called AFTER the state change has been committed, so a
    /// publish failure can never roll back a persisted transition.</summary>
    Task PublishAsync(ReservationEvent reservationEvent, CancellationToken ct = default);
}
