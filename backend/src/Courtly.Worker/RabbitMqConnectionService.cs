using Courtly.Infrastructure.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Courtly.Worker;

/// <summary>
/// Feature 17: the Worker no longer owns its own connection — it reuses the shared
/// <see cref="IRabbitMqConnection"/> (backoff retry + automatic recovery live there) so the process has a
/// single broker connection. On startup it eagerly opens that connection (a bad broker fails fast and is
/// logged), then refreshes a liveness file the Docker healthcheck reads.
/// </summary>
public sealed class RabbitMqConnectionService : BackgroundService
{
    private const string LivenessFilePath = "/tmp/worker-healthy";
    private static readonly TimeSpan LivenessInterval = TimeSpan.FromSeconds(15);

    private readonly IRabbitMqConnection _connection;
    private readonly ILogger<RabbitMqConnectionService> _logger;

    public RabbitMqConnectionService(IRabbitMqConnection connection, ILogger<RabbitMqConnectionService> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Eager connect: prove the Worker is wired to the broker (backoff retry lives in RabbitMqConnection).
        try
        {
            var connection = await _connection.GetConnectionAsync(stoppingToken);
            _logger.LogInformation(
                "Connected to RabbitMQ at {Endpoint}.", connection.Endpoint);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogCritical(ex, "Failed to connect to RabbitMQ after retries; stopping Worker.");
            throw;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RefreshLivenessAsync(stoppingToken);
            await DelayAsync(LivenessInterval, stoppingToken);
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
        // The shared connection is owned by DI (singleton); do not dispose it here.
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
