using Courtly.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Courtly.Worker;

/// <summary>
/// Feature 2 baseline: opens a single RabbitMQ connection on startup (with backoff retry) so the Worker
/// container is provably wired to the broker, and refreshes a liveness file the Docker healthcheck reads.
/// The full consumer (queues, DLX, email/notification handlers) arrives in feature 17 and will move the
/// connection into Infrastructure as the shared publisher/consumer.
/// </summary>
public sealed class RabbitMqConnectionService : BackgroundService
{
    private const string LivenessFilePath = "/tmp/worker-healthy";
    private static readonly TimeSpan LivenessInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan[] RetryBackoff =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
    ];

    private readonly RabbitOptions _options;
    private readonly ILogger<RabbitMqConnectionService> _logger;
    private IConnection? _connection;

    public RabbitMqConnectionService(IOptions<RabbitOptions> options, ILogger<RabbitMqConnectionService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _connection = await ConnectWithRetryAsync(stoppingToken);
        _logger.LogInformation(
            "Connected to RabbitMQ at {Host}:{Port}.", _options.Host, _options.Port);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_connection.IsOpen)
            {
                await RefreshLivenessAsync(stoppingToken);
            }
            else
            {
                _logger.LogWarning("RabbitMQ connection is not open; awaiting automatic recovery.");
            }

            await DelayAsync(LivenessInterval, stoppingToken);
        }
    }

    private async Task<IConnection> ConnectWithRetryAsync(CancellationToken stoppingToken)
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
            stoppingToken.ThrowIfCancellationRequested();
            try
            {
                return await factory.CreateConnectionAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                var delay = RetryBackoff[Math.Min(attempt, RetryBackoff.Length - 1)];
                attempt++;
                _logger.LogWarning(
                    ex,
                    "RabbitMQ connection attempt {Attempt} to {Host}:{Port} failed; retrying in {Delay}s.",
                    attempt, _options.Host, _options.Port, delay.TotalSeconds);
                await DelayAsync(delay, stoppingToken);
            }
        }
    }

    private async Task RefreshLivenessAsync(CancellationToken stoppingToken)
    {
        try
        {
            await File.WriteAllTextAsync(LivenessFilePath, "healthy", stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to write worker liveness file at {Path}.", LivenessFilePath);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_connection is not null)
        {
            await _connection.CloseAsync(cancellationToken);
            await _connection.DisposeAsync();
        }

        TryDeleteLivenessFile();
        await base.StopAsync(cancellationToken);
    }

    private void TryDeleteLivenessFile()
    {
        try
        {
            if (File.Exists(LivenessFilePath))
            {
                File.Delete(LivenessFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove worker liveness file at {Path}.", LivenessFilePath);
        }
    }

    private static async Task DelayAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(delay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested — swallow so the host can stop cleanly.
        }
    }
}
