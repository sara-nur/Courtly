using System.Text.Json;
using Courtly.Application.Abstractions;
using Courtly.Application.Common.Exceptions;
using Courtly.Application.Messaging;
using Courtly.Contracts.Messaging;
using Courtly.Infrastructure.Messaging;
using Courtly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Courtly.Worker;

/// <summary>
/// Feature 17 email consumer: the auxiliary <see cref="BackgroundService"/> that drains <see cref="MessagingTopology.EmailQueue"/>
/// and turns each reservation/payment event into a customer email. It owns one long-lived channel for its lifetime and
/// acks per message; a delivery is retried with bounded exponential backoff (<see cref="RetryPolicy"/>) and, once those
/// are exhausted, nacked without requeue so RabbitMQ dead-letters it to the DLQ for manual inspection (rubric Appendix A.1).
/// <para>
/// Only singleton-safe dependencies are injected (the shared connection, an <see cref="IServiceScopeFactory"/>, the logger).
/// Per-message work resolves <see cref="IEmailSender"/> and <see cref="CourtlyDbContext"/> from a fresh DI scope so the
/// scoped DbContext is never shared across messages.
/// </para>
/// </summary>
public sealed class EmailConsumerBackgroundService : BackgroundService
{
    private readonly IRabbitMqConnection _connection;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailConsumerBackgroundService> _logger;
    private IChannel? _channel;

    public EmailConsumerBackgroundService(
        IRabbitMqConnection connection,
        IServiceScopeFactory scopeFactory,
        ILogger<EmailConsumerBackgroundService> logger)
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

            // Cap unacked deliveries so a slow SMTP handshake can't pull the whole queue into memory.
            await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, stoppingToken);

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += (sender, ea) => HandleAsync(_channel, ea, stoppingToken);

            await _channel.BasicConsumeAsync(
                MessagingTopology.EmailQueue, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

            _logger.LogInformation("Email consumer attached to queue {Queue}.", MessagingTopology.EmailQueue);

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
    /// retry loop that sends the email and acks, or dead-letters once retries are exhausted.
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
                case ReservationRoutingKeys.Confirmed:
                case ReservationRoutingKeys.Cancelled:
                    reservationEvent = JsonSerializer.Deserialize<ReservationEvent>(
                        ea.Body.Span, MessagingTopology.SerializerOptions);
                    break;
                case PaymentRoutingKeys.Refunded:
                    paymentEvent = JsonSerializer.Deserialize<PaymentEvent>(
                        ea.Body.Span, MessagingTopology.SerializerOptions);
                    break;
                default:
                    _logger.LogWarning(
                        "Unknown routing key {RoutingKey} on email queue; dead-lettering.", routingKey);
                    await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, ct);
                    return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex, "Failed to deserialize email message for {RoutingKey}; dead-lettering as poison.", routingKey);
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, ct);
            return;
        }

        if (reservationEvent is null && paymentEvent is null)
        {
            // A valid-JSON null payload is still unusable — treat as poison rather than retrying forever.
            _logger.LogError("Email message for {RoutingKey} deserialized to null; dead-lettering.", routingKey);
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, ct);
            return;
        }

        // 2) Bounded retry loop: send the email and ack, or dead-letter once retries are exhausted.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                var db = scope.ServiceProvider.GetRequiredService<CourtlyDbContext>();

                if (reservationEvent is not null)
                {
                    await SendReservationEmailAsync(emailSender, db, routingKey, reservationEvent, ct);
                }
                else
                {
                    await SendRefundEmailAsync(emailSender, db, paymentEvent!, ct);
                }

                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "email handler failed attempt {Attempt} for {RoutingKey}", attempt + 1, routingKey);

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

    /// <summary>Resolves the recipient + court for a reservation event and sends the matching confirmation/cancellation email.</summary>
    private async Task SendReservationEmailAsync(
        IEmailSender emailSender, CourtlyDbContext db, string routingKey, ReservationEvent evt, CancellationToken ct)
    {
        var recipient = await LookupRecipientAsync(db, evt.UserId, ct);
        var courtName = await LookupCourtNameAsync(db, evt.CourtId, ct);

        switch (routingKey)
        {
            case ReservationRoutingKeys.Confirmed:
                await emailSender.SendBookingConfirmedAsync(
                    recipient.Email, recipient.Name, courtName,
                    evt.SlotStartUtc, evt.SlotEndUtc, evt.TotalPrice, ct);
                break;
            case ReservationRoutingKeys.Cancelled:
                await emailSender.SendBookingCancelledAsync(
                    recipient.Email, recipient.Name, courtName,
                    evt.SlotStartUtc, evt.SlotEndUtc, evt.Reason, ct);
                break;
        }
    }

    /// <summary>Resolves the recipient + the court (via the reservation) for a refund event and sends the refund email.</summary>
    private async Task SendRefundEmailAsync(
        IEmailSender emailSender, CourtlyDbContext db, PaymentEvent evt, CancellationToken ct)
    {
        var recipient = await LookupRecipientAsync(db, evt.UserId, ct);

        // PaymentEvent has no CourtId, so resolve the court through the reservation it refers to.
        var courtId = await db.Reservations
            .AsNoTracking()
            .Where(r => r.Id == evt.ReservationId)
            .Select(r => r.CourtId)
            .FirstOrDefaultAsync(ct);
        var courtName = await LookupCourtNameAsync(db, courtId, ct);

        // Prefer the amount actually charged (cents) when present; fall back to the catalog amount.
        var amount = evt.AmountChargedCents is { } cents ? cents / 100m : evt.Amount;

        await emailSender.SendPaymentRefundedAsync(recipient.Email, recipient.Name, courtName, amount, ct);
    }

    private static async Task<(string Email, string Name)> LookupRecipientAsync(
        CourtlyDbContext db, Guid userId, CancellationToken ct)
    {
        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Email, u.FirstName, u.LastName })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException($"User {userId} not found for email delivery.");

        var name = $"{user.FirstName} {user.LastName}".Trim();
        return (user.Email ?? string.Empty, name);
    }

    private static async Task<string> LookupCourtNameAsync(CourtlyDbContext db, long courtId, CancellationToken ct)
        => await db.Courts
            .AsNoTracking()
            .Where(c => c.Id == courtId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException($"Court {courtId} not found for email delivery.");

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}
