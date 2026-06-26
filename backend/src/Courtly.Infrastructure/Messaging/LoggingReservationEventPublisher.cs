using Courtly.Contracts.Messaging;
using Microsoft.Extensions.Logging;

namespace Courtly.Infrastructure.Messaging;

/// <summary>
/// The feature-14 implementation of <see cref="IReservationEventPublisher"/>: until the Worker + RabbitMQ publisher
/// land in feature 17, reservation lifecycle events are LOGGED rather than dropped, so the state machine stays
/// event-aware and "what would be published" is visible in the API logs. Feature 17 replaces only this registration
/// with the real topic-exchange publisher — every caller depends on the interface, so nothing else changes. Stateless
/// (logger only), hence safe to register as a singleton.
/// </summary>
public sealed class LoggingReservationEventPublisher : IReservationEventPublisher
{
    private readonly ILogger<LoggingReservationEventPublisher> _logger;

    public LoggingReservationEventPublisher(ILogger<LoggingReservationEventPublisher> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync(ReservationEvent reservationEvent, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Reservation event {RoutingKey}: reservation {ReservationId} (user {UserId}, court {CourtId}, status {Status}, total {Total}).",
            reservationEvent.RoutingKey, reservationEvent.ReservationId, reservationEvent.UserId,
            reservationEvent.CourtId, reservationEvent.Status, reservationEvent.TotalPrice);
        return Task.CompletedTask;
    }
}
