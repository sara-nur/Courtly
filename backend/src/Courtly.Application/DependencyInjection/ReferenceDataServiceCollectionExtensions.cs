using Courtly.Application.Abstractions;
using Courtly.Application.Reference;
using Microsoft.Extensions.DependencyInjection;

namespace Courtly.Application.DependencyInjection;

/// <summary>
/// DI wiring for the reference-data subsystem (feature 9). Each reference service is <c>Scoped</c> (it injects
/// the scoped <see cref="Courtly.Infrastructure.Persistence.CourtlyDbContext"/>). Called once from
/// <c>Program.cs</c> next to <c>AddCourtlyAuth()</c>.
/// </summary>
public static class ReferenceDataServiceCollectionExtensions
{
    public static IServiceCollection AddCourtlyReferenceData(this IServiceCollection services)
    {
        services.AddScoped<ICountryService, CountryService>();
        services.AddScoped<ICityService, CityService>();
        services.AddScoped<ISurfaceTypeService, SurfaceTypeService>();
        services.AddScoped<ICourtTypeService, CourtTypeService>();
        services.AddScoped<IAmenityService, AmenityService>();

        return services;
    }
}
