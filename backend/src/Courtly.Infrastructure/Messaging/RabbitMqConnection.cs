using Courtly.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Courtly.Infrastructure.Messaging;

/// <summary>
/// Feature 17: the shared singleton RabbitMQ connection. Lazily opens ONE <see cref="IConnection"/> on first
/// <see cref="GetConnectionAsync"/> behind a <see cref="SemaphoreSlim"/> guard, with 1/2/4/8s exponential backoff
/// (matching the feature-2 Worker baseline). <c>AutomaticRecoveryEnabled</c> keeps the connection alive across
/// broker blips. Channels are created per-caller (never shared) because <see cref="IChannel"/> is not thread-safe.
/// </summary>
public sealed class RabbitMqConnection : IRabbitMqConnection
{
    private static readonly TimeSpan[] RetryBackoff =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
    ];

    private readonly RabbitOptions _options;
    private readonly ILogger<RabbitMqConnection> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private IConnection? _connection;

    public RabbitMqConnection(IOptions<RabbitOptions> options, ILogger<RabbitMqConnection> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async ValueTask<IConnection> GetConnectionAsync(CancellationToken ct = default)
    {
        // Fast path: connection already open, no lock needed.
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        await _initLock.WaitAsync(ct);
        try
        {
            // Re-check inside the lock: another caller may have opened it while we waited.
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            _connection = await ConnectWithRetryAsync(ct);
            _logger.LogInformation(
                "Opened shared RabbitMQ connection to {Host}:{Port}.", _options.Host, _options.Port);
            return _connection;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask<IChannel> CreateChannelAsync(CancellationToken ct = default)
    {
        var connection = await GetConnectionAsync(ct);
        return await connection.CreateChannelAsync(cancellationToken: ct);
    }

    private async Task<IConnection> ConnectWithRetryAsync(CancellationToken ct)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.User,
            Password = _options.Password,
            AutomaticRecoveryEnabled = true,
        };

        var attempt = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await factory.CreateConnectionAsync(ct);
            }
            catch (Exception ex)
            {
                var delay = RetryBackoff[Math.Min(attempt, RetryBackoff.Length - 1)];
                attempt++;
                _logger.LogWarning(
                    ex,
                    "RabbitMQ connection attempt {Attempt} to {Host}:{Port} failed; retrying in {Delay}s.",
                    attempt, _options.Host, _options.Port, delay.TotalSeconds);
                await Task.Delay(delay, ct);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.CloseAsync();
            await _connection.DisposeAsync();
            _connection = null;
        }

        _initLock.Dispose();
    }
}
