using Courtly.Application.Dashboard;
using Microsoft.Extensions.DependencyInjection;

namespace Courtly.Application.DependencyInjection;

/// <summary>
/// DI wiring for the dashboard-analytics subsystem (feature 19). The service is <c>Scoped</c> (it injects the scoped
/// <see cref="Courtly.Infrastructure.Persistence.CourtlyDbContext"/>) and reuses the registered
/// <see cref="Courtly.Application.Abstractions.IMaintenanceService"/> + the shared <c>IMemoryCache</c>. Called once from
/// <c>Program.cs</c> after the reservation + payment subsystems it reads from.
/// </summary>
public static class DashboardServiceCollectionExtensions
{
    public static IServiceCollection AddCourtlyDashboard(this IServiceCollection services)
    {
        services.AddScoped<IDashboardService, DashboardService>();

        return services;
    }
}
