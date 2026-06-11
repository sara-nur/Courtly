using Courtly.Application.Abstractions;
using Courtly.Application.Auth;
using Courtly.Application.Email;
using Microsoft.Extensions.DependencyInjection;

namespace Courtly.Application.DependencyInjection;

/// <summary>DI wiring for the auth subsystem (feature 5).</summary>
public static class AuthServiceCollectionExtensions
{
    public static IServiceCollection AddCourtlyAuth(this IServiceCollection services)
    {
        // Stateless (options + clock only) → singletons, so JwtBearerOptions can resolve ITokenService at startup.
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ITokenService, TokenService>();

        // Scoped: depend on the scoped CourtlyDbContext / UserManager.
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IEmailSender, LoggingEmailSender>();

        return services;
    }
}
