using RabbitMQ.Client;

namespace Courtly.Infrastructure.Messaging;

/// <summary>
/// Feature 17: owns the single shared RabbitMQ <see cref="IConnection"/> for the process (publisher in the API,
/// consumer in the Worker). Channels are cheap and NOT thread-safe, so the connection is shared but every caller
/// creates its own channel via <see cref="CreateChannelAsync"/>. Registered as a singleton; disposing it closes
/// the connection.
/// </summary>
public interface IRabbitMqConnection : IAsyncDisposable
{
    /// <summary>Returns the shared connection, lazily opening it (with backoff) on first use.</summary>
    ValueTask<IConnection> GetConnectionAsync(CancellationToken ct = default);

    /// <summary>Creates a fresh channel on the shared connection. Caller owns disposal.</summary>
    ValueTask<IChannel> CreateChannelAsync(CancellationToken ct = default);
}
