using Courtly.Application.Abstractions;
using Courtly.Application.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace Courtly.Application.DependencyInjection;

/// <summary>DI wiring for feature 6 cross-cutting services that live in the Application layer.</summary>
public static class CrossCuttingServiceCollectionExtensions
{
    public static IServiceCollection AddCourtlyCrossCutting(this IServiceCollection services)
    {
        // Scoped: reads the request-scoped CourtlyDbContext on a cache miss (over the singleton IMemoryCache).
        services.AddScoped<IRevokedTokenCache, RevokedTokenCache>();
        return services;
    }
}
