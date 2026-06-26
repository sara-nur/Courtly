using Courtly.Application.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Courtly.Application.DependencyInjection;

/// <summary>
/// DI wiring for the admin user service (feature 15 slice; feature 15A extends it). <see cref="IUserService"/> is
/// <c>Scoped</c> (it injects the scoped <see cref="Courtly.Infrastructure.Persistence.CourtlyDbContext"/>). Called once
/// from <c>Program.cs</c> alongside the other feature registrations.
/// </summary>
public static class UsersServiceCollectionExtensions
{
    public static IServiceCollection AddCourtlyUsers(this IServiceCollection services)
    {
        services.AddScoped<IUserService, UserService>();

        return services;
    }
}
