using System.Text.Json;
using Courtly.Application.Messaging;
using Courtly.Application.Notifications;
using Courtly.Contracts.Messaging;
using Courtly.Contracts.Notifications;
using Courtly.Infrastructure.Messaging;
using Courtly.Infrastructure.Persistence;
using Courtly.Worker.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Courtly.Worker;

/// <summary>
/// Feature 18 notification consumer: the auxiliary <see cref="BackgroundService"/> that drains
/// <see cref="MessagingTopology.NotificationQueue"/> and turns each reservation/payment event into a persisted in-app
/// notification, then best-effort pushes it over SignalR via the API. It owns one long-lived channel for its lifetime
/// and acks per message; the persist is retried with bounded exponential backoff (<see cref="RetryPolicy"/>) and, once
/// those are exhausted, nacked without requeue so RabbitMQ dead-letters it to the DLQ for manual inspection.
/// <para>
/// The notification is persisted EXACTLY ONCE per delivery. The SignalR push runs after a successful persist and is
/// best-effort — a push failure is logged and swallowed (the row is already persisted and REST polling is the fallback),
/// never re-persisting the notification.
/// </para>
/// <para>
/// Only singleton-safe dependencies are injected (the shared connection, an <see cref="IServiceScopeFactory"/>, the
/// logger). Per-message work resolves <see cref="INotificationWriter"/>, <see cref="CourtlyDbContext"/> and
/// <see cref="IInternalPushClient"/> from a fresh DI scope so the scoped DbContext is never shared across messages.
/// </para>
/// </summary>
public sealed class NotificationConsumerBackgroundService : BackgroundService
{
    private readonly IRabbitMqConnection _connection;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationConsumerBackgroundService> _logger;
    private IChannel? _channel;

    public NotificationConsumerBackgroundService(
        IRabbitMqConnection connection,
        IServiceScopeFactory scopeFactory,
        ILogger<NotificationConsumerBackgroundService> logger)
    {
        _connection = connection;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _channel = await _connection.CreateChannelAsync(stoppingToken);

            // Declare the shared topology so the queue exists regardless of which side (API/Worker) boots first.
            await RabbitMqTopology.DeclareAsync(_channel, stoppingToken);

            // Cap unacked deliveries so a slow persist/push can't pull the whole queue into memory.
            await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, stoppingToken);

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += (sender, ea) => HandleAsync(_channel, ea, stoppingToken);

            await _channel.BasicConsumeAsync(
                MessagingTopology.NotificationQueue, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

            _logger.LogInformation(
                "Notification consumer attached to queue {Queue}.", MessagingTopology.NotificationQueue);

            // Keep the channel alive for the service lifetime; deliveries arrive on the consumer callback.
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested — swallow so the host can stop cleanly.
        }
    }

    /// <summary>
    /// Handles one delivery: deserialize by routing key (poison messages go straight to the DLQ), then run the bounded
    /// retry loop that persists the notification and acks, or dead-letters once retries are exhausted. After a
    /// successful persist the just-created notification is pushed over SignalR exactly once (best-effort).
    /// </summary>
    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs ea, CancellationToken ct)
    {
        var routingKey = ea.RoutingKey;

        // 1) Deserialize by routing key. An unknown key or malformed body is poison — nack without requeue (→ DLQ).
        ReservationEvent? reservationEvent = null;
        PaymentEvent? paymentEvent = null;
        try
        {
            switch (routingKey)
            {
                case ReservationRoutingKeys.Created:
                case ReservationRoutingKeys.Confirmed:
                case ReservationRoutingKeys.Cancelled:
                case ReservationRoutingKeys.Rescheduled:
                case ReservationRoutingKeys.Completed:
                    reservationEvent = JsonSerializer.Deserialize<ReservationEvent>(
                        ea.Body.Span, MessagingTopology.SerializerOptions);
                    break;
                case PaymentRoutingKeys.Succeeded:
                case PaymentRoutingKeys.Refunded:
                    paymentEvent = JsonSerializer.Deserialize<PaymentEvent>(
                        ea.Body.Span, MessagingTopology.SerializerOptions);
                    break;
                default:
                    _logger.LogWarning(
                        "Unknown routing key {RoutingKey} on notification queue; dead-lettering.", routingKey);
                    await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, ct);
                    return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex, "Failed to deserialize notification message for {RoutingKey}; dead-lettering as poison.", routingKey);
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, ct);
            return;
        }

        if (reservationEvent is null && paymentEvent is null)
        {
            // A valid-JSON null payload is still unusable — treat as poison rather than retrying forever.
            _logger.LogError("Notification message for {RoutingKey} deserialized to null; dead-lettering.", routingKey);
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, ct);
            return;
        }

        var userId = reservationEvent?.UserId ?? paymentEvent!.UserId;

        // 2) Bounded retry loop: persist the notification (fresh scope per attempt) and ack, or dead-letter once
        //    retries are exhausted. The notification is persisted EXACTLY ONCE — the push below never re-persists.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                NotificationDto dto;
                using (var scope = _scopeFactory.CreateScope())
                {
                    var writer = scope.ServiceProvider.GetRequiredService<INotificationWriter>();
                    var db = scope.ServiceProvider.GetRequiredService<CourtlyDbContext>();
                    var pushClient = scope.ServiceProvider.GetRequiredService<IInternalPushClient>();

                    var courtName = await ResolveCourtNameAsync(db, reservationEvent, paymentEvent, ct);
                    var (type, title, text) =
                        NotificationFactory.Build(routingKey, reservationEvent, paymentEvent, courtName);

                    dto = await writer.CreateAsync(userId, type, title, text, ct);

                    // 3) Best-effort SignalR push exactly once after the persist succeeded. A failure here is logged and
                    //    swallowed: the row is persisted and REST polling is the fallback, so we never re-persist.
                    try
                    {
                        await pushClient.PushAsync(new InternalPushRequest(userId, dto), ct);
                    }
                    catch (Exception pushEx)
                    {
                        _logger.LogWarning(
                            pushEx,
                            "Best-effort SignalR push failed for notification {NotificationId} ({RoutingKey}); persisted row remains and REST polling is the fallback.",
                            dto.Id, routingKey);
                    }
                }

                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "notification handler failed attempt {Attempt} for {RoutingKey}", attempt + 1, routingKey);

                if (RetryPolicy.ShouldDeadLetter(attempt))
                {
                    _logger.LogError("retries exhausted; dead-lettering {RoutingKey}", routingKey);
                    await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, ct);
                    return;
                }

                await Task.Delay(RetryPolicy.DelayForAttempt(attempt), ct);
            }
        }
    }

    /// <summary>
    /// Resolves the court name for the notification text. A reservation event carries its own <c>CourtId</c>; a payment
    /// event has none, so the court is resolved through the reservation it refers to (exactly as the email consumer's
    /// refund path). Returns null when no court can be found — the factory then uses a generic fallback.
    /// </summary>
    private static async Task<string?> ResolveCourtNameAsync(
        CourtlyDbContext db, ReservationEvent? reservationEvent, PaymentEvent? paymentEvent, CancellationToken ct)
    {
        long courtId;
        if (reservationEvent is not null)
        {
            courtId = reservationEvent.CourtId;
        }
        else
        {
            // PaymentEvent has no CourtId, so resolve the court through the reservation it refers to.
            courtId = await db.Reservations
                .AsNoTracking()
                .Where(r => r.Id == paymentEvent!.ReservationId)
                .Select(r => r.CourtId)
                .FirstOrDefaultAsync(ct);
        }

        return await db.Courts
            .AsNoTracking()
            .Where(c => c.Id == courtId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(ct);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}
