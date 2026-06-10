using Courtly.Domain.Entities;
using Courtly.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Courtly.Infrastructure.Persistence;

/// <summary>DI wiring for the runtime data seeder (feature 4).</summary>
public static class SeedingServiceCollectionExtensions
{
    public static IServiceCollection AddCourtlySeeding(this IServiceCollection services)
    {
        // PBKDF2 hasher used to hash seeded passwords; the same hasher Identity resolves for sign-in (feature 5).
        services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
        services.AddSingleton<SeedImageLoader>();
        // Scoped: depends on the scoped CourtlyDbContext.
        services.AddScoped<ICourtlyDataSeeder, CourtlyDataSeeder>();
        return services;
    }
}
