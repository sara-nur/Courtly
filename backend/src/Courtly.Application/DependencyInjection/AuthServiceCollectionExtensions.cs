using Courtly.Application.Abstractions;
using Courtly.Application.Auth;
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
        // The email transport (IEmailSender) is chosen by each host: AddCourtlySmtpEmail() in both the
        // API (password-reset link, feature 22) and the Worker (booking mail, feature 17). Kept out of
        // here so there is no duplicate IEmailSender registration (rubric §3.4).

        return services;
    }
}
