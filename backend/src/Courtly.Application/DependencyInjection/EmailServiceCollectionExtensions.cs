using Courtly.Application.Abstractions;
using Courtly.Application.Email;
using Microsoft.Extensions.DependencyInjection;

namespace Courtly.Application.DependencyInjection;

/// <summary>DI wiring for the real SMTP email sender (feature 17).</summary>
public static class EmailServiceCollectionExtensions
{
    /// <summary>
    /// Registers the real SMTP sender as <see cref="IEmailSender"/>. Both hosts wire it: the Worker for booking
    /// mail (feature 17) and the API for the password-reset link email (feature 22). Scoped to match the other
    /// request-scoped services.
    /// </summary>
    public static IServiceCollection AddCourtlySmtpEmail(this IServiceCollection services)
    {
        services.AddScoped<IEmailSender, SmtpEmailSender>();

        return services;
    }
}
