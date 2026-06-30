using Courtly.Application.Reports;
using Microsoft.Extensions.DependencyInjection;

namespace Courtly.Application.DependencyInjection;

/// <summary>
/// DI wiring for the PDF-reports subsystem (feature 20). The service is <c>Scoped</c> (it injects the scoped
/// <see cref="Courtly.Infrastructure.Persistence.CourtlyDbContext"/>) and reuses the registered
/// <see cref="Courtly.Application.Abstractions.IMaintenanceService"/>. The QuestPDF Community licence is configured in
/// <see cref="ReportService"/>'s static constructor (so it is set whether or not DI is used). Called from
/// <c>Program.cs</c> after the dashboard the reports reconcile with.
/// </summary>
public static class ReportsServiceCollectionExtensions
{
    public static IServiceCollection AddCourtlyReports(this IServiceCollection services)
    {
        services.AddScoped<IReportService, ReportService>();

        return services;
    }
}
