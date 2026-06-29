using Courtly.Contracts.Messaging;
using RabbitMQ.Client;

namespace Courtly.Infrastructure.Messaging;

/// <summary>
/// Declares the RabbitMQ topology described by <see cref="MessagingTopology"/> on a channel. Both the API publisher and
/// the Worker consumer call this on startup so the exchanges/queues exist regardless of which side boots first; every
/// declaration is idempotent — re-declaring with identical arguments is a no-op (rubric §3.4: shared, drift-free wiring).
/// <para>
/// IMPORTANT: every argument here (durability, exchange type, the <c>x-dead-letter-exchange</c> queue argument) MUST be
/// identical on both declarers. RabbitMQ rejects a re-declaration whose parameters differ from the existing entity with
/// a channel-level <c>PRECONDITION_FAILED</c>, so keep this the single source for those values.
/// </para>
/// </summary>
public static class RabbitMqTopology
{
    /// <summary>
    /// Idempotently declares the events topic exchange, the dead-letter fanout exchange, the email queue (dead-lettering
    /// to <see cref="MessagingTopology.DeadLetterExchange"/>) and its dead-letter queue, then binds them per
    /// <see cref="MessagingTopology.EmailBindingKeys"/>. Safe to call from both the publisher and the consumer.
    /// </summary>
    public static async Task DeclareAsync(IChannel channel, CancellationToken ct = default)
    {
        // Durable topic exchange carrying every reservation/payment event keyed by its routing key.
        await channel.ExchangeDeclareAsync(
            MessagingTopology.EventsExchange, ExchangeType.Topic,
            durable: true, autoDelete: false, arguments: null, cancellationToken: ct);

        // Durable fanout exchange that receives messages dead-lettered after retries are exhausted.
        await channel.ExchangeDeclareAsync(
            MessagingTopology.DeadLetterExchange, ExchangeType.Fanout,
            durable: true, autoDelete: false, arguments: null, cancellationToken: ct);

        // Email queue: durable + dead-lettered to the fanout DLX so exhausted messages land in the DLQ.
        await channel.QueueDeclareAsync(
            MessagingTopology.EmailQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = MessagingTopology.DeadLetterExchange,
            },
            cancellationToken: ct);

        // Durable dead-letter queue holding exhausted messages for manual inspection.
        await channel.QueueDeclareAsync(
            MessagingTopology.EmailDeadLetterQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: null, cancellationToken: ct);

        // The fanout DLX has no routing semantics, so the DLQ binds with the empty routing key.
        await channel.QueueBindAsync(
            MessagingTopology.EmailDeadLetterQueue, MessagingTopology.DeadLetterExchange,
            routingKey: string.Empty, arguments: null, cancellationToken: ct);

        // Bind each email-triggering routing key from the topic exchange to the email queue.
        foreach (var key in MessagingTopology.EmailBindingKeys)
        {
            await channel.QueueBindAsync(
                MessagingTopology.EmailQueue, MessagingTopology.EventsExchange,
                routingKey: key, arguments: null, cancellationToken: ct);
        }
    }
}
