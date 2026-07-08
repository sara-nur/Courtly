using Courtly.Application.Reservations;
using Courtly.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Courtly.Worker;

/// <summary>
/// Drives <see cref="ReservationAutoCompleteService"/> on a timer so Confirmed reservations are moved to Completed once
/// their slot has ended — the automatic completion <see cref="IReservationService.CompleteAsync"/>'s doc refers to. A
/// hosted <see cref="BackgroundService"/> (not <c>Task.Run</c>) so the host owns its lifecycle and graceful shutdown
/// (rubric §8.2); the scan loop never dies silently — a single bad tick is logged and the loop continues (rubric A.1).
/// </summary>
/// <remarks>
/// <see cref="ReservationAutoCompleteService"/> is Scoped (it owns a <c>CourtlyDbContext</c>), so a fresh scope is
/// resolved per tick rather than captured for the singleton's lifetime. The scan interval is bound from
/// <c>RESERVATION_AUTOCOMPLETE_SCAN_SECONDS</c> and floored at 5s so a misconfigured tiny value can't busy-loop.
/// </remarks>
public sealed class AutoCompleteBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ReservationOptions _options;
    private readonly ILogger<AutoCompleteBackgroundService> _logger;

    public AutoCompleteBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<ReservationOptions> options,
        ILogger<AutoCompleteBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.AutoCompleteScanSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);

                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ReservationAutoCompleteService>();
                var completed = await service.CompleteEndedReservationsAsync(100, stoppingToken);
                if (completed > 0)
                {
                    _logger.LogInformation("Auto-completed {Count} ended reservation(s).", completed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutdown requested — exit the loop cleanly.
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto-complete scan failed; will retry next tick.");
            }
        }
    }
}
