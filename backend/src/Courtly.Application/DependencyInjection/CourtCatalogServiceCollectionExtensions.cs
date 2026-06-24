using Courtly.Application.Abstractions;
using Courtly.Application.Courts;
using Microsoft.Extensions.DependencyInjection;

namespace Courtly.Application.DependencyInjection;

/// <summary>
/// DI wiring for the court-catalog subsystem (feature 10). The service is <c>Scoped</c> (it injects the scoped
/// <see cref="Courtly.Infrastructure.Persistence.CourtlyDbContext"/>). Validators need no per-validator
/// registration — the <c>Program.cs</c> assembly scan over the <c>Courtly.Application</c> assembly picks them up.
/// Called once from <c>Program.cs</c> right after <c>AddCourtlyReferenceData()</c>.
/// </summary>
public static class CourtCatalogServiceCollectionExtensions
{
    public static IServiceCollection AddCourtlyCourtCatalog(this IServiceCollection services)
    {
        services.AddScoped<ICourtService, CourtService>();

        return services;
    }
}
