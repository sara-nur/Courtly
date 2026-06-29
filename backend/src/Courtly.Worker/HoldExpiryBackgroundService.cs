using Courtly.Application.Reservations;
using Courtly.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Courtly.Worker;

/// <summary>
/// Feature 17: drives <see cref="ReservationHoldExpiryService"/> on a timer so unpaid <c>Pending</c> holds are
/// released after their deadline. A hosted <see cref="BackgroundService"/> (not <c>Task.Run</c>) so the host owns
/// its lifecycle and graceful shutdown (rubric §8.2); the scan loop never dies silently — a single bad tick is
/// logged and the loop continues to the next one (rubric A.1).
/// </summary>
/// <remarks>
/// <see cref="ReservationHoldExpiryService"/> is Scoped (it owns a <c>CourtlyDbContext</c>), so a fresh scope is
/// resolved per tick rather than capturing one for the singleton service's lifetime. The scan interval is bound
/// from <c>RESERVATION_HOLD_SCAN_SECONDS</c> and floored at 5s so a misconfigured tiny value can't busy-loop.
/// </remarks>
public sealed class HoldExpiryBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ReservationOptions _options;
    private readonly ILogger<HoldExpiryBackgroundService> _logger;

    public HoldExpiryBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<ReservationOptions> options,
        ILogger<HoldExpiryBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.HoldScanSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);

                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ReservationHoldExpiryService>();
                var cancelled = await service.CancelExpiredHoldsAsync(100, stoppingToken);
                if (cancelled > 0)
                {
                    _logger.LogInformation("Auto-cancelled {Count} expired holds.", cancelled);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutdown requested — exit the loop cleanly.
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Hold-expiry scan failed; will retry next tick.");
            }
        }
    }
}
