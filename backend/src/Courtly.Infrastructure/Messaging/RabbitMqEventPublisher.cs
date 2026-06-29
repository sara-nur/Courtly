using System.Text.Json;
using Courtly.Contracts.Messaging;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Courtly.Infrastructure.Messaging;

/// <summary>
/// The feature-17 implementation of both <see cref="IReservationEventPublisher"/> and
/// <see cref="IPaymentEventPublisher"/>: it replaces the two logging stubs and publishes events to the
/// <see cref="MessagingTopology.EventsExchange"/> topic exchange over the one shared <see cref="IRabbitMqConnection"/>.
/// Registered as a singleton (a channel is opened per publish, since channels are not thread-safe). Topology is declared
/// exactly once, lazily, on the first publish so the broker need not be primed before the API starts. Because every
/// caller invokes this AFTER committing the state change (a post-commit seam), a broker hiccup must NEVER fail the
/// user's HTTP request — every publish failure is logged and swallowed.
/// </summary>
public sealed class RabbitMqEventPublisher : IReservationEventPublisher, IPaymentEventPublisher
{
    private readonly IRabbitMqConnection _connection;
    private readonly ILogger<RabbitMqEventPublisher> _logger;

    // Guards the one-time topology declaration: the SemaphoreSlim serialises the first concurrent publishes so the
    // exchanges/queues are declared once, and the flag lets every later publish skip the lock on the fast path.
    private readonly SemaphoreSlim _topologyGate = new(1, 1);
    private volatile bool _topologyDeclared;

    public RabbitMqEventPublisher(IRabbitMqConnection connection, ILogger<RabbitMqEventPublisher> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public Task PublishAsync(ReservationEvent reservationEvent, CancellationToken ct = default) =>
        PublishCoreAsync(reservationEvent.RoutingKey, reservationEvent, ct);

    public Task PublishAsync(PaymentEvent paymentEvent, CancellationToken ct = default) =>
        PublishCoreAsync(paymentEvent.RoutingKey, paymentEvent, ct);

    private async Task PublishCoreAsync(string routingKey, object payload, CancellationToken ct)
    {
        try
        {
            await EnsureTopologyAsync(ct);

            // Serialise with the concrete record type so every property emits (a static object cast would only emit
            // System.Object members), keeping DLQ payloads fully inspectable.
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType(), MessagingTopology.SerializerOptions);

            await using var channel = await _connection.CreateChannelAsync(ct);
            var props = new BasicProperties { Persistent = true, ContentType = "application/json" };
            await channel.BasicPublishAsync(
                MessagingTopology.EventsExchange, routingKey, mandatory: false, basicProperties: props,
                body: (ReadOnlyMemory<byte>)bytes, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            // Post-commit seam: the state change is already persisted, so a broker failure must not surface to the
            // caller. Log loudly (the DLQ won't help here — nothing reached the broker) and swallow.
            _logger.LogError(ex, "Failed to publish event {RoutingKey} to exchange {Exchange}.",
                routingKey, MessagingTopology.EventsExchange);
        }
    }

    private async Task EnsureTopologyAsync(CancellationToken ct)
    {
        if (_topologyDeclared)
        {
            return;
        }

        await _topologyGate.WaitAsync(ct);
        try
        {
            if (_topologyDeclared)
            {
                return;
            }

            await using var channel = await _connection.CreateChannelAsync(ct);
            await RabbitMqTopology.DeclareAsync(channel, ct);
            _topologyDeclared = true;
        }
        finally
        {
            _topologyGate.Release();
        }
    }
}
