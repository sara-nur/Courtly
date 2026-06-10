using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Courtly.Infrastructure.Persistence;

/// <summary>DI wiring for the Courtly EF Core context.</summary>
public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddCourtlyPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<CourtlyDbContext>(options => ConfigureCourtlyDbContext(options, connectionString));
        return services;
    }

    /// <summary>
    /// Single source of truth for the DbContext options, called by both runtime DI and the design-time factory
    /// so the generated migration always matches the runtime model (same provider, same naming convention).
    /// </summary>
    public static void ConfigureCourtlyDbContext(DbContextOptionsBuilder options, string connectionString)
    {
        options
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(CourtlyDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention();
    }
}
